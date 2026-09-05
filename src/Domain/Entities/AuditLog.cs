namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class AuditLog : TenantOwnedEntity
{
    public AuditLog(
        Guid id,
        Guid tenantId,
        Guid? actorUserId,
        string eventType,
        DateTimeOffset occurredAtUtc)
        : base(id, tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        ActorUserId = actorUserId;
        EventType = eventType;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid? ActorUserId { get; private set; }

    public string EventType { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
