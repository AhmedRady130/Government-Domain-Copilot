namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed record ApprovalExecutionResult(
    string RequestId,
    bool Success,
    string Status,
    string Message,
    DateTimeOffset? ExecutedAt = null);
