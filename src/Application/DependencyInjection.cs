using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Embeddings;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GovernmentDomainCopilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IIngestDocumentUseCase, IngestDocumentUseCase>();
        services.AddScoped<IEmbeddingService, ResilientEmbeddingService>();
        services.AddScoped<IChunkEmbeddingService, ChunkEmbeddingService>();
        services.AddScoped<IVectorSearchUseCase, VectorSearchUseCase>();
        services.AddSingleton<ReciprocalRankFusionService>();
        services.AddSingleton<IRetrievalReranker, WeightedSignalReranker>();
        services.AddScoped<IHybridSearchUseCase, HybridSearchUseCase>();
        return services;
    }
}
