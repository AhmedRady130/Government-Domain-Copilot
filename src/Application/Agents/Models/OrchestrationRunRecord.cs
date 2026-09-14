namespace GovernmentDomainCopilot.Application.Agents.Models;

using GovernmentDomainCopilot.Application.Answering.Models;

public sealed record OrchestrationRunRecord(
    string RunId,
    string CorrelationId,
    Guid TenantId,
    string PatternName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TimeSpan Duration,
    string Status,
    int IterationCount,
    IReadOnlyList<AgentExecutionRecord> AgentExecutions,
    bool UsedFallback,
    string? FallbackReason,
    GroundedAnswerResponse? FinalResponse,
    ApprovalRequest? PendingApproval,
    string? FailureReason = null,
    string? SessionId = null);
