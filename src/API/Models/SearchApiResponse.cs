namespace GovernmentDomainCopilot.API.Models;

public sealed record SearchResultItemApiResponse(
    Guid ChunkId,
    Guid DocumentId,
    int Sequence,
    string Title,
    string SourceReference,
    string Content,
    double? Distance,
    double? KeywordScore,
    double RrfScore,
    int Rank);

public sealed record SearchApiResponse(
    int TopK,
    int TotalReturned,
    double DurationMs,
    string ProviderName,
    string ModelName,
    IReadOnlyList<SearchResultItemApiResponse> Items);
