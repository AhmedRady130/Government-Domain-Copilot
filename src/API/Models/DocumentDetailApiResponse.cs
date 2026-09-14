namespace GovernmentDomainCopilot.API.Models;

public sealed record DocumentDetailApiResponse(
    Guid DocumentId,
    string Title,
    string SourceReference,
    string Status,
    int ChunkCount,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc);
