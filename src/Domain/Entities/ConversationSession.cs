namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class ConversationSession : TenantOwnedEntity
{
    public ConversationSession(Guid id, Guid tenantId, Guid userId, DateTimeOffset createdAtUtc)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        UserId = userId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
