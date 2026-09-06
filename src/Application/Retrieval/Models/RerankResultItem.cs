namespace GovernmentDomainCopilot.Application.Retrieval.Models;

/// <summary>
/// A single result item after deterministic reranking.
/// Preserves all retrieval signals from RRF fusion for diagnostics transparency.
/// </summary>
public sealed record RerankResultItem(
    Guid ChunkId,
    Guid DocumentId,
    int Sequence,
    string Title,
    string SourceReference,
    string Content,

    /// <summary>
    /// Raw cosine distance from pgvector (range 0.0 to 2.0). Lower = closer.
    /// Null if the chunk was not returned by the vector branch (keyword-only match).
    /// </summary>
    double? Distance,

    /// <summary>
    /// PostgreSQL ts_rank keyword relevance score. Null if not in the keyword branch.
    /// </summary>
    double? KeywordScore,

    /// <summary>
    /// Reciprocal Rank Fusion score from the fusion stage (k=60).
    /// </summary>
    double RrfScore,

    /// <summary>
    /// 1-based rank position assigned by RRF fusion, before deterministic reranking.
    /// Preserved for diagnostics. Use FinalRank for the definitive ordering.
    /// </summary>
    int Rank,

    /// <summary>
    /// Weighted signal reranking score.
    /// Formula: 0.50 * NormRrfScore + 0.30 * VectorCloseness + 0.20 * NormKeywordScore.
    /// These weights are MVP heuristics, not empirically benchmarked.
    /// </summary>
    double RerankScore,

    /// <summary>
    /// 1-based rank position after deterministic reranking. This is the definitive ordering.
    /// </summary>
    int FinalRank);
