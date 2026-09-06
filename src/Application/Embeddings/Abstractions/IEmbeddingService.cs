using GovernmentDomainCopilot.Application.Embeddings.Models;

namespace GovernmentDomainCopilot.Application.Embeddings.Abstractions;

/// <summary>
/// High-level resilient application service for embedding generation supporting primary and fallback provider routing.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates vector embeddings using the configured primary provider with automatic fallback routing.
    /// </summary>
    /// <param name="request">The typed embedding request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed embedding generation result.</returns>
    Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken);
}
