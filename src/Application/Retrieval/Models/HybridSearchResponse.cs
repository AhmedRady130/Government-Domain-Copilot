namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record HybridSearchResponse(
    int TopK,
    int TotalReturned,
    TimeSpan Duration,
    string ProviderName,
    string ModelName,
    IReadOnlyList<RerankResultItem> Items);
