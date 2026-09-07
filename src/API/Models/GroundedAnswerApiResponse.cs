namespace GovernmentDomainCopilot.API.Models;

public sealed record GroundedAnswerApiRequest(
    string Query,
    int? TopK = null);

public sealed record CitationItemApiResponse(
    string CitationId,
    Guid ChunkId,
    Guid DocumentId,
    string SourceReference,
    string Title,
    int Sequence);

public sealed record GroundedAnswerApiResponse(
    string Status,
    string? Answer,
    string? Reason,
    IReadOnlyList<CitationItemApiResponse> Citations,
    string ProviderName,
    string ModelName,
    double DurationMs);
