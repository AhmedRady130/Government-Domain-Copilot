namespace GovernmentDomainCopilot.API.Models;

using GovernmentDomainCopilot.Application.Agents.Models;

public sealed record OrchestrationApiRequest(
    string Query,
    string? CorrelationId = null,
    string? SessionId = null);

public sealed record AgentExecutionDto(
    string AgentRole,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMs,
    bool Success,
    string OutputSummary,
    string? ErrorMessage);

public sealed record PendingApprovalDto(
    string RequestId,
    Guid TenantId,
    string ProposedAction,
    string OriginalPayload,
    string? EditedPayload,
    string Decision,
    string? ReviewerComments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    bool IsExecuted,
    DateTimeOffset? ExecutedAt);

public sealed record OrchestrationApiResponse(
    string RunId,
    string CorrelationId,
    Guid TenantId,
    string PatternName,
    string Status,
    int IterationCount,
    double DurationMs,
    bool UsedFallback,
    string? FallbackReason,
    string? Answer,
    IReadOnlyList<CitationItemApiResponse> Citations,
    IReadOnlyList<AgentExecutionDto> AgentExecutions,
    PendingApprovalDto? PendingApproval,
    string? FailureReason,
    string? SessionId = null);

public sealed record ApprovalDecisionApiRequest(
    string Decision,
    string? Comments = null,
    string? EditedPayload = null);

public sealed record ApprovalExecutionApiResponse(
    string RequestId,
    bool Success,
    string Status,
    string Message,
    DateTimeOffset? ExecutedAt);
