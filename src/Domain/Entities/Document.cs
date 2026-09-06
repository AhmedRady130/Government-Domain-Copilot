namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Document : TenantOwnedEntity
{
    public Document(
        Guid id,
        Guid tenantId,
        string title,
        string sourceReference,
        DateTimeOffset createdAtUtc,
        DocumentIngestionStatus ingestionStatus = DocumentIngestionStatus.Pending)
        : base(id, tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);

        if (!Enum.IsDefined(ingestionStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(ingestionStatus), ingestionStatus, "Invalid document ingestion status.");
        }

        Title = title;
        SourceReference = sourceReference;
        CreatedAtUtc = createdAtUtc;
        IngestionStatus = ingestionStatus;
    }

    public string Title { get; private set; }

    public string SourceReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DocumentIngestionStatus IngestionStatus { get; private set; }

    /// <summary>
    /// Transitions the document ingestion status to <see cref="DocumentIngestionStatus.Completed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the current status does not permit transitioning to Completed.</exception>
    public void MarkAsCompleted()
    {
        TransitionTo(DocumentIngestionStatus.Completed);
    }

    /// <summary>
    /// Transitions the document ingestion status to <see cref="DocumentIngestionStatus.Failed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the current status does not permit transitioning to Failed.</exception>
    public void MarkAsFailed()
    {
        TransitionTo(DocumentIngestionStatus.Failed);
    }

    /// <summary>
    /// Transitions the document ingestion status to <paramref name="newStatus"/>.
    /// </summary>
    /// <param name="newStatus">The target ingestion status.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newStatus"/> is an undefined enum value.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transition is invalid according to domain lifecycle rules.</exception>
    public void TransitionTo(DocumentIngestionStatus newStatus)
    {
        if (!Enum.IsDefined(newStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(newStatus), newStatus, "Invalid document ingestion status.");
        }

        if (!IngestionStatus.CanTransitionTo(newStatus))
        {
            throw new InvalidOperationException(
                $"Cannot transition document ingestion status from '{IngestionStatus}' to '{newStatus}'.");
        }

        IngestionStatus = newStatus;
    }
}
