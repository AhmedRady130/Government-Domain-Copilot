namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class User : TenantOwnedEntity
{
    public User(Guid id, Guid tenantId, string externalId, string displayName, DateTimeOffset createdAtUtc)
        : base(id, tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        ExternalId = externalId;
        DisplayName = displayName;
        CreatedAtUtc = createdAtUtc;
    }

    public string ExternalId { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
