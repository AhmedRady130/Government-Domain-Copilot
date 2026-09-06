using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using GovernmentDomainCopilot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, DevelopmentTenantContext>();

        services.AddSingleton<IDocumentChunker, DeterministicDocumentChunker>();
        services.AddScoped<DocumentRepository>();
        services.AddScoped<IDocumentRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IChunkEmbeddingRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IChunkRetriever, PgVectorChunkRetriever>();
        services.AddScoped<IKeywordChunkRetriever, PgKeywordChunkRetriever>();

        services.AddHttpClient<GeminiEmbeddingProvider>();
        services.AddHttpClient<OllamaEmbeddingProvider>();

        services.AddSingleton<IEmbeddingProvider, GeminiEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider, OllamaEmbeddingProvider>();

        return services;
    }
}
