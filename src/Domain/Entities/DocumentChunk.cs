namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class DocumentChunk : TenantOwnedEntity
{
    /// <summary>
    /// Maximum allowed length in characters for a single chunk's text content.
    /// </summary>
    public const int MaxContentLength = 8_000;

    public DocumentChunk(Guid id, Guid tenantId, Guid documentId, int sequence, string content)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Chunk content length ({content.Length}) exceeds maximum allowed length of {MaxContentLength} characters.",
                nameof(content));
        }

        DocumentId = documentId;
        Sequence = sequence;
        Content = content;
    }

    public Guid DocumentId { get; private set; }

    public int Sequence { get; private set; }

    public string Content { get; private set; }

    /// <summary>
    /// The persisted embedding vector for this chunk.
    /// Null until the chunk has been embedded.
    /// The vector is represented as a plain <see cref="float"/> array; no provider-specific SDK
    /// types are introduced into Domain.
    /// </summary>
    public float[]? Embedding { get; private set; }

    /// <summary>
    /// Sets the embedding vector for this chunk after validating its dimension.
    /// </summary>
    /// <param name="vector">The embedding vector. Must not be null or empty.</param>
    /// <param name="expectedDimension">
    /// The configured expected dimension size (e.g. 768). Must match the actual length of <paramref name="vector"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="vector"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="vector"/> is empty or its length does not match <paramref name="expectedDimension"/>.
    /// </exception>
    public void SetEmbedding(float[] vector, int expectedDimension)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length == 0)
        {
            throw new ArgumentException("Embedding vector must not be empty.", nameof(vector));
        }

        if (vector.Length != expectedDimension)
        {
            throw new ArgumentException(
                $"Embedding vector dimension {vector.Length} does not match expected dimension {expectedDimension}.",
                nameof(vector));
        }

        Embedding = vector;
    }
}
