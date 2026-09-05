namespace GovernmentDomainCopilot.Application.Documents.Models;

/// <summary>
/// Represents a single chunk of normalised text produced by <see cref="IDocumentChunker"/>.
/// </summary>
/// <remarks>
/// Sequence numbers are zero-based and contiguous within a document.
/// The handler maps each <see cref="ChunkData"/> to a
/// <see cref="Domain.Entities.DocumentChunk"/> domain entity before persistence.
/// </remarks>
/// <param name="Sequence">Zero-based position of the chunk within its parent document.</param>
/// <param name="Content">The normalised text content of the chunk. Never empty.</param>
public sealed record ChunkData(int Sequence, string Content);
