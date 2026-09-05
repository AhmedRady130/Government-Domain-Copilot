namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class DocumentChunk : TenantOwnedEntity
{
    public DocumentChunk(Guid id, Guid tenantId, Guid documentId, int sequence, string content)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        DocumentId = documentId;
        Sequence = sequence;
        Content = content;
    }

    public Guid DocumentId { get; private set; }

    public int Sequence { get; private set; }

    public string Content { get; private set; }
}
