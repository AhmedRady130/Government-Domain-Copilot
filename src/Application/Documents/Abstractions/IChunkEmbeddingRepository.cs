namespace GovernmentDomainCopilot.Application.Documents.Abstractions;

/// <summary>
/// Provides tenant-scoped persistence of embedding vectors for existing <see cref="Domain.Entities.DocumentChunk"/> records.
/// </summary>
public interface IChunkEmbeddingRepository
{
    /// <summary>
    /// Persists embedding vectors for the specified chunk IDs.
    /// </summary>
    /// <remarks>
    /// <para>Each chunk ID must belong to <paramref name="tenantId"/>. Any chunk ID that is not found or
    /// belongs to a different tenant causes the operation to fail with <see cref="InvalidOperationException"/>.</para>
    /// <para>Dimension of every vector must match <paramref name="expectedDimension"/>; mismatches are rejected
    /// before any write is performed.</para>
    /// <para>Writing the same embedding twice is idempotent.</para>
    /// </remarks>
    /// <param name="tenantId">The owning tenant. All chunk IDs must belong to this tenant.</param>
    /// <param name="embeddings">Ordered list of (chunk ID, vector) pairs to persist.</param>
    /// <param name="expectedDimension">The required vector dimension. Every vector must have this exact length.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="embeddings"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any chunk ID is not found, belongs to a different tenant, or a vector dimension is wrong.
    /// </exception>
    Task PersistEmbeddingsAsync(
        Guid tenantId,
        IReadOnlyList<(Guid ChunkId, float[] Vector)> embeddings,
        int expectedDimension,
        CancellationToken cancellationToken);
}
