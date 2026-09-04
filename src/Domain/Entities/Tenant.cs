namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Tenant
{
    public Tenant(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
