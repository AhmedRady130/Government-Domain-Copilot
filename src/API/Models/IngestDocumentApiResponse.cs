namespace GovernmentDomainCopilot.API.Models;

/// <summary>
/// Typed API response for successful document ingestion.
/// </summary>
/// <param name="DocumentId">Unique identifier assigned to the persisted document.</param>
/// <param name="ChunkCount">Total number of chunks created and persisted.</param>
/// <param name="Status">Ingestion completion status.</param>
public sealed record IngestDocumentApiResponse(
    Guid DocumentId,
    int ChunkCount,
    string Status);
