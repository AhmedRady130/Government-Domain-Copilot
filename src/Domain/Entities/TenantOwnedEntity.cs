namespace GovernmentDomainCopilot.Domain.Entities;

public abstract class TenantOwnedEntity
{
    protected TenantOwnedEntity(Guid id, Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);

        Id = id;
        TenantId = tenantId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }
}
