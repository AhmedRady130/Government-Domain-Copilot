namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed record ToolExecutionResult(
    bool Success,
    string OutputJson,
    string? ErrorMessage = null,
    bool RequiresApproval = false,
    string? ApprovalRequestId = null);
