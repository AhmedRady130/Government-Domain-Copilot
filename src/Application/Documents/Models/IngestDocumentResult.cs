using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Documents.Models;

/// <summary>
/// The outcome of a document ingestion operation.
/// </summary>
/// <param name="DocumentId">
/// The unique identifier assigned to the persisted <see cref="Document"/>.
/// </param>
/// <param name="ChunkCount">
/// The total number of <see cref="DocumentChunk"/> records persisted for the document.
/// </param>
/// <param name="Status">
/// The final lifecycle status of the document ingestion operation (<see cref="DocumentIngestionStatus.Completed"/> or <see cref="DocumentIngestionStatus.Failed"/>).
/// </param>
/// <param name="FailureReason">
/// Optional safe failure reason if ingestion failed.
/// </param>
public sealed record IngestDocumentResult(
    Guid DocumentId,
    int ChunkCount,
    DocumentIngestionStatus Status = DocumentIngestionStatus.Completed,
    string? FailureReason = null);
