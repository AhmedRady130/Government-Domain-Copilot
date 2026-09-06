namespace GovernmentDomainCopilot.Application.Retrieval.Abstractions;

using GovernmentDomainCopilot.Application.Retrieval.Models;

public interface IChunkRetriever
{
    Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
        Guid tenantId,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken);
}
