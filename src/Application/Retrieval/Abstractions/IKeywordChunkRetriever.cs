namespace GovernmentDomainCopilot.Application.Retrieval.Abstractions;

using GovernmentDomainCopilot.Application.Retrieval.Models;

public interface IKeywordChunkRetriever
{
    Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
        Guid tenantId,
        string query,
        int topK,
        CancellationToken cancellationToken);
}
