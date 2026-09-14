namespace GovernmentDomainCopilot.Application.Sessions.Models;

using GovernmentDomainCopilot.Application.Answering.Models;

public sealed record SessionMessageRecord(
    string MessageId,
    string SessionId,
    Guid TenantId,
    string Role,
    string Content,
    string Status,
    IReadOnlyList<CitationItem> Citations,
    string? LinkedRunId,
    DateTimeOffset Timestamp);
