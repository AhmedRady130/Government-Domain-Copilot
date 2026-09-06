namespace GovernmentDomainCopilot.Application.Retrieval.Services;

using GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed class ReciprocalRankFusionService
{
    public const int DefaultK = 60;

    public IReadOnlyList<HybridSearchResultItem> Fuse(
        IReadOnlyList<VectorSearchResultItem> vectorResults,
        IReadOnlyList<KeywordSearchResultItem> keywordResults,
        int k = DefaultK)
    {
        ArgumentNullException.ThrowIfNull(vectorResults);
        ArgumentNullException.ThrowIfNull(keywordResults);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var map = new Dictionary<Guid, (
            Guid ChunkId,
            Guid DocumentId,
            int Sequence,
            string Title,
            string SourceReference,
            string Content,
            double? Distance,
            double? KeywordScore,
            double RrfScore)>();

        for (int i = 0; i < vectorResults.Count; i++)
        {
            var v = vectorResults[i];
            int rank = i + 1; // 1-based rank
            double score = 1.0 / (k + rank);

            map[v.ChunkId] = (
                v.ChunkId,
                v.DocumentId,
                v.Sequence,
                v.Title,
                v.SourceReference,
                v.Content,
                v.Distance,
                null,
                score);
        }

        for (int i = 0; i < keywordResults.Count; i++)
        {
            var kw = keywordResults[i];
            int rank = i + 1; // 1-based rank
            double score = 1.0 / (k + rank);

            if (map.TryGetValue(kw.ChunkId, out var existing))
            {
                map[kw.ChunkId] = (
                    existing.ChunkId,
                    existing.DocumentId,
                    existing.Sequence,
                    existing.Title,
                    existing.SourceReference,
                    existing.Content,
                    existing.Distance,
                    kw.KeywordScore,
                    existing.RrfScore + score);
            }
            else
            {
                map[kw.ChunkId] = (
                    kw.ChunkId,
                    kw.DocumentId,
                    kw.Sequence,
                    kw.Title,
                    kw.SourceReference,
                    kw.Content,
                    null,
                    kw.KeywordScore,
                    score);
            }
        }

        var sorted = map.Values
            .OrderByDescending(x => x.RrfScore)
            .ThenBy(x => x.ChunkId)
            .ToList();

        var fused = new List<HybridSearchResultItem>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            fused.Add(new HybridSearchResultItem(
                item.ChunkId,
                item.DocumentId,
                item.Sequence,
                item.Title,
                item.SourceReference,
                item.Content,
                item.Distance,
                item.KeywordScore,
                item.RrfScore,
                Rank: i + 1));
        }

        return fused;
    }
}
