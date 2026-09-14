using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Models;
using GovernmentDomainCopilot.Application.Observability.Services;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;
using GovernmentDomainCopilot.Infrastructure.LLM.Providers;
using GovernmentDomainCopilot.Infrastructure.Observability;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using GovernmentDomainCopilot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GovernmentDomainCopilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GovernmentDomainCopilot")
            ?? throw new InvalidOperationException(
                "The 'GovernmentDomainCopilot' connection string must be configured.");

        services.AddDbContext<GovernmentDomainCopilotDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector()));

        services.Configure<ChunkingOptions>(
            configuration.GetSection(ChunkingOptions.SectionName));

        services.Configure<EmbeddingProviderOptions>(
            configuration.GetSection(EmbeddingProviderOptions.SectionName));

        services.Configure<LlmProviderOptions>(
            configuration.GetSection(LlmProviderOptions.SectionName));

        services.Configure<GovernmentDomainCopilot.Application.Agents.Models.OrchestrationOptions>(
            configuration.GetSection(GovernmentDomainCopilot.Application.Agents.Models.OrchestrationOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<GovernmentDomainCopilot.Infrastructure.Auth.CurrentUserContext>();
        services.AddScoped<ICurrentUserContext>(sp => sp.GetRequiredService<GovernmentDomainCopilot.Infrastructure.Auth.CurrentUserContext>());
        services.AddScoped<DevelopmentTenantContext>();

        services.AddScoped<ITenantContext>(sp =>
        {
            var hostEnv = sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
            if (hostEnv != null && !hostEnv.IsDevelopment())
            {
                // In Production: strictly resolve from authenticated CurrentUserContext
                return sp.GetRequiredService<GovernmentDomainCopilot.Infrastructure.Auth.CurrentUserContext>();
            }

            // In Development / Test: use DevelopmentTenantContext (prefers authenticated claims, safe dev fallback)
            return sp.GetRequiredService<DevelopmentTenantContext>();
        });

        // Authentication & Authorization (FR-8)
        services.AddAuthentication(GovernmentDomainCopilot.Infrastructure.Auth.ApiKeyAuthenticationOptions.SchemeName)
            .AddScheme<GovernmentDomainCopilot.Infrastructure.Auth.ApiKeyAuthenticationOptions, GovernmentDomainCopilot.Infrastructure.Auth.ApiKeyAuthenticationHandler>(
                GovernmentDomainCopilot.Infrastructure.Auth.ApiKeyAuthenticationOptions.SchemeName, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("SupervisorOnly", policy => policy.RequireRole(GovernmentDomainCopilot.Domain.Constants.Roles.Supervisor));
            options.AddPolicy("OfficerOrSupervisor", policy => policy.RequireRole(
                GovernmentDomainCopilot.Domain.Constants.Roles.Officer,
                GovernmentDomainCopilot.Domain.Constants.Roles.Supervisor));
        });

        services.AddSingleton<IDocumentChunker, DeterministicDocumentChunker>();
        services.AddScoped<DocumentRepository>();
        services.AddScoped<IDocumentRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IChunkEmbeddingRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IChunkRetriever, PgVectorChunkRetriever>();
        services.AddScoped<IKeywordChunkRetriever, PgKeywordChunkRetriever>();

        services.AddHttpClient<GeminiEmbeddingProvider>();
        services.AddHttpClient<OllamaEmbeddingProvider>();
        services.AddHttpClient<GeminiChatCompletionProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmProviderOptions>>().Value;
            if (options.HttpTimeoutSeconds > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
            }
        });

        services.AddSingleton<IEmbeddingProvider, GeminiEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider, OllamaEmbeddingProvider>();
        services.AddScoped<GovernmentDomainCopilot.Application.Answering.Abstractions.IChatCompletionProvider>(sp =>
            sp.GetRequiredService<GeminiChatCompletionProvider>());

        // Durable PostgreSQL-backed Session History & Run Trace Stores (FR-7)
        services.AddScoped<GovernmentDomainCopilot.Application.Sessions.Abstractions.ISessionStore, GovernmentDomainCopilot.Infrastructure.Sessions.PostgresSessionStore>();
        services.AddScoped<GovernmentDomainCopilot.Application.Traces.Abstractions.IRunTraceStore, GovernmentDomainCopilot.Infrastructure.Traces.PostgresRunTraceStore>();

        // Observability: Correlation context, cost calculator, LLM trace store (FR-9)
        // ICorrelationContext is scoped (one per HTTP request / logical scope)
        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        // ModelPricingOptions from configuration (section "ModelPricing")
        services.Configure<ModelPricingOptions>(
            configuration.GetSection(ModelPricingOptions.SectionName));

        // ICostCalculator is stateless; singleton is safe
        services.AddSingleton<ICostCalculator, ModelPricingCostCalculator>();

        // ILlmTraceStore → PostgresLlmTraceStore for ALL runtime environments (Development + Production).
        // InMemoryLlmTraceStore must NEVER be registered here; it is test-only and injected via test DI overrides.
        services.AddScoped<ILlmTraceStore, PostgresLlmTraceStore>();

        return services;
    }
}
