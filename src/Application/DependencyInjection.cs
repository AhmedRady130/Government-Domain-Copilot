using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Embeddings;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GovernmentDomainCopilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IIngestDocumentUseCase, IngestDocumentUseCase>();
        services.AddScoped<IEmbeddingService, ResilientEmbeddingService>();
        services.AddScoped<IChunkEmbeddingService, ChunkEmbeddingService>();
        return services;
    }
}
