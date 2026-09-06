namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record KeywordSearchResultItem(
    Guid ChunkId,
    Guid DocumentId,
    int Sequence,
    string Title,
    string SourceReference,
    string Content,
    double KeywordScore,
    int Rank);
