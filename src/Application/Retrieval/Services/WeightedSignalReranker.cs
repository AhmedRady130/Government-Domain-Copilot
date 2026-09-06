namespace GovernmentDomainCopilot.Application.Retrieval.Services;

using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;

/// <summary>
/// Deterministic retrieval reranker using a weighted combination of normalized signals.
///
/// Algorithm (MVP heuristics — not empirically benchmarked):
///   RerankScore = w_rrf * NormRrfScore
///               + w_vec * VectorCloseness
///               + w_kw  * NormKeywordScore
///
/// Signal normalization:
///   NormRrfScore     = RrfScore / max(RrfScore in candidate set)
///   VectorCloseness  = 1 - Distance / 2.0        (range [0,1]; missing → 0.0)
///   NormKeywordScore = KeywordScore / max(KeywordScore in candidate set)   (missing → 0.0)
///
/// Weights: RRF=0.50, Vector=0.30, Keyword=0.20 (see ADR-0007 for rationale).
///
/// Tie-breaking (deterministic):
///   1. RerankScore DESC
///   2. RrfScore DESC
///   3. Rank (pre-rerank) ASC
///   4. ChunkId ASC
/// </summary>
public sealed class WeightedSignalReranker : IRetrievalReranker
{
    /// <summary>Weight given to the normalised RRF fusion score.</summary>
    public const double RrfWeight = 0.50;

    /// <summary>Weight given to vector closeness (1 − distance/2).</summary>
    public const double VectorWeight = 0.30;

    /// <summary>Weight given to the normalised keyword score.</summary>
    public const double KeywordWeight = 0.20;

    /// <summary>Human-readable version tag for observability logging.</summary>
    public const string RerankerName = "WeightedSignalReranker-v1";

    public IReadOnlyList<RerankResultItem> Rerank(RerankRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Candidates.Count == 0)
        {
            return Array.Empty<RerankResultItem>();
        }

        // --- Compute normalization denominators ---
        double maxRrfScore = 0.0;
        double maxKeywordScore = 0.0;

        foreach (var c in request.Candidates)
        {
            if (c.RrfScore > maxRrfScore)
            {
                maxRrfScore = c.RrfScore;
            }

            if (c.KeywordScore.HasValue && c.KeywordScore.Value > maxKeywordScore)
            {
                maxKeywordScore = c.KeywordScore.Value;
            }
        }

        // Guard: if all RRF scores are 0 (degenerate input), every normalized value is 0.
        // This preserves the original ordering via tie-breaking rather than silently fabricating scores.

        // --- Score each candidate ---
        var scored = new (HybridSearchResultItem Item, double RerankScore)[request.Candidates.Count];

        for (int i = 0; i < request.Candidates.Count; i++)
        {
            var candidate = request.Candidates[i];

            double normRrf = maxRrfScore > 0.0
                ? Math.Clamp(candidate.RrfScore / maxRrfScore, 0.0, 1.0)
                : 0.0;

            // Cosine distance from pgvector is in [0, 2].
            // Closeness = clamp(1 - d/2, 0, 1) maps it to [0, 1] (higher = closer).
            // Missing Distance (keyword-only chunk) → closeness = 0.0.
            double vectorCloseness = candidate.Distance.HasValue
                ? Math.Clamp(1.0 - candidate.Distance.Value / 2.0, 0.0, 1.0)
                : 0.0;

            double normKeyword = (maxKeywordScore > 0.0 && candidate.KeywordScore.HasValue)
                ? Math.Clamp(candidate.KeywordScore.Value / maxKeywordScore, 0.0, 1.0)
                : 0.0;

            double rerankScore = RrfWeight * normRrf
                               + VectorWeight * vectorCloseness
                               + KeywordWeight * normKeyword;

            scored[i] = (candidate, rerankScore);
        }

        // --- Deterministic ordering ---
        Array.Sort(scored, (a, b) =>
        {
            // 1. RerankScore DESC
            int cmp = b.RerankScore.CompareTo(a.RerankScore);
            if (cmp != 0) return cmp;

            // 2. RrfScore DESC
            cmp = b.Item.RrfScore.CompareTo(a.Item.RrfScore);
            if (cmp != 0) return cmp;

            // 3. Pre-rerank Rank ASC
            cmp = a.Item.Rank.CompareTo(b.Item.Rank);
            if (cmp != 0) return cmp;

            // 4. ChunkId ASC (Guid lexicographic — arbitrary but stable)
            return a.Item.ChunkId.CompareTo(b.Item.ChunkId);
        });

        // --- Assign FinalRank (1-based) ---
        var result = new List<RerankResultItem>(scored.Length);
        for (int i = 0; i < scored.Length; i++)
        {
            var (item, rerankScore) = scored[i];
            result.Add(new RerankResultItem(
                item.ChunkId,
                item.DocumentId,
                item.Sequence,
                item.Title,
                item.SourceReference,
                item.Content,
                item.Distance,
                item.KeywordScore,
                item.RrfScore,
                item.Rank,
                rerankScore,
                FinalRank: i + 1));
        }

        return result;
    }
}
