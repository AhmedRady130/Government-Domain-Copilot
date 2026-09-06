using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Infrastructure.Documents;

/// <summary>
/// Configuration options for the deterministic document chunker.
/// </summary>
public sealed class ChunkingOptions
{
    /// <summary>
    /// Configuration section name in application settings.
    /// </summary>
    public const string SectionName = "Ingestion:Chunking";

    private int _chunkSize = 1_000;
    private int _chunkOverlap = 100;

    /// <summary>
    /// Maximum character length per chunk.
    /// Default is 1,000 characters. Must be greater than 0 and less than or equal to <see cref="DocumentChunk.MaxContentLength"/>.
    /// </summary>
    public int ChunkSize
    {
        get => _chunkSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "ChunkSize must be greater than zero.");
            }

            if (value > DocumentChunk.MaxContentLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"ChunkSize cannot exceed DocumentChunk.MaxContentLength ({DocumentChunk.MaxContentLength}).");
            }

            _chunkSize = value;
        }
    }

    /// <summary>
    /// Character overlap between consecutive chunks.
    /// Default is 100 characters. Must be non-negative and strictly less than <see cref="ChunkSize"/>.
    /// </summary>
    public int ChunkOverlap
    {
        get => _chunkOverlap;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "ChunkOverlap must be non-negative.");
            }

            _chunkOverlap = value;
        }
    }
}
