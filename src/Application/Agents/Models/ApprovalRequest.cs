namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed class ApprovalRequest
{
    public string RequestId { get; }
    public Guid TenantId { get; }
    public string ProposedAction { get; }
    public string OriginalPayload { get; }
    public string? EditedPayload { get; private set; }
    public ApprovalDecision Decision { get; private set; }
    public string? ReviewerComments { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public bool IsExecuted { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public string? ExecutionSummary { get; private set; }

    public ApprovalRequest(
        string requestId,
        Guid tenantId,
        string proposedAction,
        string originalPayload)
    {
        RequestId = requestId;
        TenantId = tenantId;
        ProposedAction = proposedAction;
        OriginalPayload = originalPayload;
        Decision = ApprovalDecision.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(string? comments = null)
    {
        if (Decision != ApprovalDecision.Pending)
            throw new InvalidOperationException($"Cannot approve request in '{Decision}' status.");

        Decision = ApprovalDecision.Approved;
        ReviewerComments = comments;
        DecidedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string reason)
    {
        if (Decision != ApprovalDecision.Pending)
            throw new InvalidOperationException($"Cannot reject request in '{Decision}' status.");

        Decision = ApprovalDecision.Rejected;
        ReviewerComments = reason;
        DecidedAt = DateTimeOffset.UtcNow;
    }

    public void Edit(string editedPayload, string? comments = null)
    {
        if (Decision != ApprovalDecision.Pending)
            throw new InvalidOperationException($"Cannot edit request in '{Decision}' status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(editedPayload);
        EditedPayload = editedPayload;
        Decision = ApprovalDecision.Edited;
        ReviewerComments = comments;
        DecidedAt = DateTimeOffset.UtcNow;
    }

    public void MarkExecuted(string? summary = null)
    {
        if (Decision != ApprovalDecision.Approved && Decision != ApprovalDecision.Edited)
            throw new InvalidOperationException($"Cannot execute consequential action in status '{Decision}'. Consequential side effects require explicit approval.");

        if (IsExecuted)
            throw new InvalidOperationException("Consequential action has already been executed.");

        IsExecuted = true;
        ExecutedAt = DateTimeOffset.UtcNow;
        ExecutionSummary = summary ?? "Consequential action safely staged and recorded.";
    }
}
