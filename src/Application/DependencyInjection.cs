using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Services;
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
        services.AddSingleton<IEvidenceSufficiencyPolicy, EvidenceSufficiencyPolicy>();
        services.AddSingleton<ICitationValidator, CitationValidator>();
        services.AddScoped<IGroundedAnswerUseCase, GroundedAnswerUseCase>();

        // Evaluation services
        services.AddSingleton<GovernmentDomainCopilot.Application.Evaluation.Abstractions.IGoldenDatasetLoader, GovernmentDomainCopilot.Application.Evaluation.Services.GoldenDatasetLoader>();
        services.AddSingleton<GovernmentDomainCopilot.Application.Evaluation.Abstractions.IEvaluationMetricCalculator, GovernmentDomainCopilot.Application.Evaluation.Services.EvaluationMetricCalculator>();
        services.AddSingleton<GovernmentDomainCopilot.Application.Evaluation.Abstractions.IEvaluationTenantContext, GovernmentDomainCopilot.Application.Evaluation.Services.EvaluationTenantContext>();
        services.AddScoped<GovernmentDomainCopilot.Application.Evaluation.Abstractions.IEvaluationHarness, GovernmentDomainCopilot.Application.Evaluation.Services.EvaluationHarness>();

        return services;
    }
}
