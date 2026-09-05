namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Document : TenantOwnedEntity
{
    public Document(Guid id, Guid tenantId, string title, string sourceReference, DateTimeOffset createdAtUtc)
        : base(id, tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);

        Title = title;
        SourceReference = sourceReference;
        CreatedAtUtc = createdAtUtc;
    }

    public string Title { get; private set; }

    public string SourceReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
