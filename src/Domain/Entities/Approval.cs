namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Approval : TenantOwnedEntity
{
    public Approval(Guid id, Guid tenantId, Guid runId, string status, DateTimeOffset requestedAtUtc)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        RunId = runId;
        Status = status;
        RequestedAtUtc = requestedAtUtc;
    }

    public Guid RunId { get; private set; }

    public string Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }
}
