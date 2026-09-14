namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class User : TenantOwnedEntity
{
    public User(Guid id, Guid tenantId, string externalId, string displayName, DateTimeOffset createdAtUtc, string role = "Officer")
        : base(id, tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        ExternalId = externalId;
        DisplayName = displayName;
        CreatedAtUtc = createdAtUtc;
        Role = role;
    }

    public string ExternalId { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string Role { get; private set; }
}
