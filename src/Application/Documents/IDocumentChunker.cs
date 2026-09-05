using GovernmentDomainCopilot.Application.Documents.Models;

namespace GovernmentDomainCopilot.Application.Documents;

/// <summary>
/// Splits normalised source text into an ordered, deterministic sequence of
/// <see cref="ChunkData"/> values for subsequent persistence as
/// <see cref="Domain.Entities.DocumentChunk"/> records.
/// </summary>
/// <remarks>
/// The chunking strategy (window size, overlap, boundary logic) is an implementation
/// detail owned by Infrastructure and registered via DI.
/// This interface exists in Application to decouple the ingestion use-case handler
/// from any specific chunking algorithm or external SDK.
///
/// Contract guarantees required of every implementation:
/// <list type="bullet">
///   <item>The returned list contains at least one element for any non-empty input.</item>
///   <item>Sequence numbers start at zero and are strictly contiguous (0, 1, 2, …).</item>
///   <item>No <see cref="ChunkData.Content"/> value is null, empty, or whitespace-only.</item>
///   <item>Identical input always produces identical output (deterministic).</item>
/// </list>
/// </remarks>
public interface IDocumentChunker
{
    /// <summary>
    /// Splits <paramref name="normalizedText"/> into an ordered list of chunks.
    /// </summary>
    /// <param name="normalizedText">
    /// Pre-normalised plain text (trimmed, Unicode NFC, collapsed internal whitespace).
    /// Must not be null, empty, or whitespace-only.
    /// </param>
    /// <returns>
    /// A read-only list of <see cref="ChunkData"/> ordered by ascending
    /// <see cref="ChunkData.Sequence"/>.
    /// </returns>
    IReadOnlyList<ChunkData> Chunk(string normalizedText);
}
