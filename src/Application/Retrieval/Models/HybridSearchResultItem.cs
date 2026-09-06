namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record HybridSearchResultItem(
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
