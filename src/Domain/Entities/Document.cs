namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Document : TenantOwnedEntity
{
    /// <summary>
    /// Maximum allowed length in characters for a failure reason description.
    /// </summary>
    public const int MaxFailureReasonLength = 500;

    public Document(
        Guid id,
        Guid tenantId,
        string title,
        string sourceReference,
        DateTimeOffset createdAtUtc,
        DocumentIngestionStatus ingestionStatus = DocumentIngestionStatus.Pending,
        string? failureReason = null)
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
        FailureReason = ingestionStatus == DocumentIngestionStatus.Failed
            ? SanitizeFailureReason(failureReason)
            : null;
    }

    public string Title { get; private set; }

    public string SourceReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DocumentIngestionStatus IngestionStatus { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// Transitions the document ingestion status to <see cref="DocumentIngestionStatus.Completed"/> and clears failure reason.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the current status does not permit transitioning to Completed.</exception>
    public void MarkAsCompleted()
    {
        TransitionTo(DocumentIngestionStatus.Completed);
    }

    /// <summary>
    /// Transitions the document ingestion status to <see cref="DocumentIngestionStatus.Failed"/> with an optional safe failure reason.
    /// </summary>
    /// <param name="failureReason">Optional diagnostic reason describing why ingestion failed.</param>
    /// <exception cref="InvalidOperationException">Thrown when the current status does not permit transitioning to Failed.</exception>
    public void MarkAsFailed(string? failureReason = null)
    {
        TransitionTo(DocumentIngestionStatus.Failed, failureReason);
    }

    /// <summary>
    /// Transitions the document ingestion status to <paramref name="newStatus"/>.
    /// </summary>
    /// <param name="newStatus">The target ingestion status.</param>
    /// <param name="failureReason">Optional failure reason if transitioning to Failed.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newStatus"/> is an undefined enum value.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transition is invalid according to domain lifecycle rules.</exception>
    public void TransitionTo(DocumentIngestionStatus newStatus, string? failureReason = null)
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
        FailureReason = newStatus == DocumentIngestionStatus.Failed
            ? SanitizeFailureReason(failureReason)
            : null;
    }

    private static string? SanitizeFailureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Ingestion processing failed.";
        }

        var clean = reason.Trim();

        var stackTraceIdx = clean.IndexOf("\n   at ", StringComparison.Ordinal);
        if (stackTraceIdx >= 0)
        {
            clean = clean[..stackTraceIdx].Trim();
        }

        stackTraceIdx = clean.IndexOf("   at ", StringComparison.Ordinal);
        if (stackTraceIdx >= 0)
        {
            clean = clean[..stackTraceIdx].Trim();
        }

        if (clean.Length > MaxFailureReasonLength)
        {
            clean = clean[..MaxFailureReasonLength];
        }

        return clean;
    }
}
