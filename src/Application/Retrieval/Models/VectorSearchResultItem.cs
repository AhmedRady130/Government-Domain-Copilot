namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record VectorSearchResultItem
{
    public VectorSearchResultItem(
        Guid chunkId,
        Guid documentId,
        int sequence,
        string title,
        string sourceReference,
        string content,
        double distance,
        int rank)
    {
        ChunkId = chunkId;
        DocumentId = documentId;
        Sequence = sequence;
        Title = title;
        SourceReference = sourceReference;
        Content = content;
        Distance = distance;
        Rank = rank;
    }

    public Guid ChunkId { get; }
    public Guid DocumentId { get; }
    public int Sequence { get; }
    public string Title { get; }
    public string SourceReference { get; }
    public string Content { get; }

    /// <summary>
    /// Raw cosine distance returned by pgvector (range 0.0 to 2.0).
    /// Lower values indicate closer vector similarity / better match.
    /// </summary>
    public double Distance { get; }

    /// <summary>
    /// 1-based rank position within the ordered retrieval result set.
    /// </summary>
    public int Rank { get; }
}
