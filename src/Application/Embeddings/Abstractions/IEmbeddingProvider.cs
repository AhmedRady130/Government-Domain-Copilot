using GovernmentDomainCopilot.Application.Embeddings.Models;

namespace GovernmentDomainCopilot.Application.Embeddings.Abstractions;

/// <summary>
/// Defines a provider-independent contract for generating vector embeddings.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the unique identifier/name for this embedding provider (e.g. "Gemini", "Ollama").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates vector embeddings for the provided input request.
    /// </summary>
    /// <param name="request">The typed embedding request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed embedding generation result.</returns>
    Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken);
}
