namespace GovernmentDomainCopilot.Application.Agents.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;

public interface IApprovalManager
{
    Task<ApprovalRequest> CreateRequestAsync(
        Guid tenantId,
        string proposedAction,
        string payload,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest?> GetRequestAsync(
        string requestId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> SubmitDecisionAsync(
        string requestId,
        Guid tenantId,
        ApprovalDecision decision,
        string? comments = null,
        string? editedPayload = null,
        CancellationToken cancellationToken = default);

    Task<ApprovalExecutionResult> ExecuteActionAsync(
        string requestId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalRequest>> ListRequestsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ApprovalRequest>>(Array.Empty<ApprovalRequest>());
}
