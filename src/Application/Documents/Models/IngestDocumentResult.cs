namespace GovernmentDomainCopilot.Application.Documents.Models;

/// <summary>
/// The successful outcome of a document ingestion operation.
/// </summary>
/// <param name="DocumentId">
/// The unique identifier assigned to the newly persisted <see cref="Domain.Entities.Document"/>.
/// </param>
/// <param name="ChunkCount">
/// The total number of <see cref="Domain.Entities.DocumentChunk"/> records persisted
/// for the document. Always greater than zero on success.
/// </param>
public sealed record IngestDocumentResult(Guid DocumentId, int ChunkCount);
