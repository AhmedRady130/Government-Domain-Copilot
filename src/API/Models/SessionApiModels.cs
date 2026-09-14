namespace GovernmentDomainCopilot.API.Models;

public sealed record CreateSessionApiRequest(
    string? Title = null);

public sealed record SessionApiResponse(
    string SessionId,
    Guid TenantId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    string Status);

public sealed record SessionMessageApiResponse(
    string MessageId,
    string SessionId,
    string Role,
    string Content,
    string Status,
    IReadOnlyList<CitationItemApiResponse> Citations,
    string? LinkedRunId,
    DateTimeOffset Timestamp);

public sealed record PostSessionMessageApiRequest(
    string Content,
    string? Mode = "answer",
    string? CorrelationId = null);
