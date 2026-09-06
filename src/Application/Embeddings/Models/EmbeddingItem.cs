namespace GovernmentDomainCopilot.Application.Embeddings.Models;

/// <summary>
/// Represents an individual generated vector embedding item.
/// </summary>
/// <param name="Index">Zero-based sequence index matching the input array.</param>
/// <param name="Vector">The normalized floating-point embedding vector values.</param>
public sealed record EmbeddingItem(int Index, IReadOnlyList<float> Vector);
