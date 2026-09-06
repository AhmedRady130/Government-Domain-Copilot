using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
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
            options.UseNpgsql(connectionString));

        services.Configure<ChunkingOptions>(
            configuration.GetSection(ChunkingOptions.SectionName));

        services.AddSingleton<IDocumentChunker, DeterministicDocumentChunker>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        return services;
    }
}
