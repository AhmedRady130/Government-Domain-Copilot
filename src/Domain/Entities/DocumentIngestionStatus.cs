namespace GovernmentDomainCopilot.Domain.Entities;

/// <summary>
/// Represents the strongly typed lifecycle status of a document during ingestion.
/// </summary>
public enum DocumentIngestionStatus
{
    /// <summary>
    /// The document has been created and is pending ingestion processing.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// The document and its associated chunks have been successfully ingested and persisted.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The document ingestion pipeline failed due to a validation, normalisation, or persistence error.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Provides domain rules and transition guards for <see cref="DocumentIngestionStatus"/>.
/// </summary>
public static class DocumentIngestionStatusExtensions
{
    /// <summary>
    /// Determines whether a transition from <paramref name="current"/> status to <paramref name="next"/> status is allowed.
    /// </summary>
    /// <remarks>
    /// Allowed state transitions:
    /// <list type="bullet">
    ///   <item><description><c>Pending</c> -> <c>Completed</c></description></item>
    ///   <item><description><c>Pending</c> -> <c>Failed</c></description></item>
    /// </list>
    /// Terminal states (<c>Completed</c>, <c>Failed</c>) permit no further state transitions.
    /// </remarks>
    /// <param name="current">The current status of the document.</param>
    /// <param name="next">The target status to transition to.</param>
    /// <returns><see langword="true"/> if the transition is allowed by domain rules; otherwise, <see langword="false"/>.</returns>
    public static bool CanTransitionTo(this DocumentIngestionStatus current, DocumentIngestionStatus next)
    {
        return current switch
        {
            DocumentIngestionStatus.Pending => next is DocumentIngestionStatus.Completed or DocumentIngestionStatus.Failed,
            DocumentIngestionStatus.Completed => false,
            DocumentIngestionStatus.Failed => false,
            _ => false
        };
    }
}
