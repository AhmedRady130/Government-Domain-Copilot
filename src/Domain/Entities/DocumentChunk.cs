namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class DocumentChunk : TenantOwnedEntity
{
    /// <summary>
    /// Maximum allowed length in characters for a single chunk's text content.
    /// </summary>
    public const int MaxContentLength = 8_000;

    public DocumentChunk(Guid id, Guid tenantId, Guid documentId, int sequence, string content)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Chunk content length ({content.Length}) exceeds maximum allowed length of {MaxContentLength} characters.",
                nameof(content));
        }

        DocumentId = documentId;
        Sequence = sequence;
        Content = content;
    }

    public Guid DocumentId { get; private set; }

    public int Sequence { get; private set; }

    public string Content { get; private set; }
}
