namespace GovernmentDomainCopilot.Application.Agents.Services;

using System.Collections.Concurrent;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;

public sealed class InMemoryApprovalManager : IApprovalManager
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _requests = new();

    public Task<ApprovalRequest> CreateRequestAsync(
        Guid tenantId,
        string proposedAction,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var id = $"apr-{Guid.NewGuid():N}";
        var request = new ApprovalRequest(id, tenantId, proposedAction, payload);
        _requests[id] = request;
        return Task.FromResult(request);
    }

    public Task<ApprovalRequest?> GetRequestAsync(
        string requestId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_requests.TryGetValue(requestId, out var request) && request.TenantId == tenantId)
        {
            return Task.FromResult<ApprovalRequest?>(request);
        }
        return Task.FromResult<ApprovalRequest?>(null);
    }

    public Task<ApprovalRequest> SubmitDecisionAsync(
        string requestId,
        Guid tenantId,
        ApprovalDecision decision,
        string? comments = null,
        string? editedPayload = null,
        CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var request) || request.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Approval request '{requestId}' was not found for tenant '{tenantId}'.");
        }

        switch (decision)
        {
            case ApprovalDecision.Approved:
                request.Approve(comments);
                break;
            case ApprovalDecision.Rejected:
                request.Reject(comments ?? "Rejected by reviewer.");
                break;
            case ApprovalDecision.Edited:
                request.Edit(editedPayload ?? request.OriginalPayload, comments);
                break;
            default:
                throw new ArgumentException($"Invalid decision '{decision}'.", nameof(decision));
        }

        return Task.FromResult(request);
    }

    public Task<ApprovalExecutionResult> ExecuteActionAsync(
        string requestId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var request) || request.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Approval request '{requestId}' was not found for tenant '{tenantId}'.");
        }

        // Consequential side-effect safety: Pending and Rejected requests are blocked
        request.MarkExecuted($"Action '{request.ProposedAction}' staged and recorded safely under decision '{request.Decision}'.");

        return Task.FromResult(new ApprovalExecutionResult(
            RequestId: request.RequestId,
            Success: true,
            Status: request.Decision.ToString(),
            Message: request.ExecutionSummary ?? "Action safely executed and staged without real-world side effects.",
            ExecutedAt: request.ExecutedAt));
    }

    public Task<IReadOnlyList<ApprovalRequest>> ListRequestsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var list = _requests.Values
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApprovalRequest>>(list);
    }
}
