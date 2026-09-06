using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Embeddings.Abstractions;

/// <summary>
/// Orchestrates embedding generation for <see cref="DocumentChunk"/> records and persists them to the vector store.
/// </summary>
public interface IChunkEmbeddingService
{
    /// <summary>
    /// Generates embeddings for the provided chunks using the configured <see cref="IEmbeddingService"/>
    /// and persists them via <see cref="Documents.Abstractions.IChunkEmbeddingRepository"/>.
    /// </summary>
    /// <param name="tenantId">The owning tenant ID. Must match the TenantId of all chunks.</param>
    /// <param name="chunks">The list of document chunks to embed and persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="chunks"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="chunks"/> is empty or contains cross-tenant chunks.</exception>
    Task EmbedAndPersistChunksAsync(
        Guid tenantId,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);
}
