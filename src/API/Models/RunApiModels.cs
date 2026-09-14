namespace GovernmentDomainCopilot.API.Models;

public sealed record RunSummaryApiResponse(
    string RunId,
    string CorrelationId,
    Guid TenantId,
    string PatternName,
    string Status,
    int IterationCount,
    double DurationMs,
    bool UsedFallback,
    string? FallbackReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? SessionId);

public sealed record RunDetailApiResponse(
    string RunId,
    string CorrelationId,
    Guid TenantId,
    string PatternName,
    string Status,
    int IterationCount,
    double DurationMs,
    bool UsedFallback,
    string? FallbackReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? SessionId,
    string? Answer,
    IReadOnlyList<CitationItemApiResponse> Citations,
    IReadOnlyList<AgentExecutionDto> AgentExecutions,
    PendingApprovalDto? PendingApproval,
    string? FailureReason);
