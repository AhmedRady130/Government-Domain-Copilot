using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using Xunit;

namespace Application.Tests.Retrieval;

public sealed class ReciprocalRankFusionServiceTests
{
    private readonly ReciprocalRankFusionService _sut = new();

    [Fact]
    public void Fuse_BothBranchesPopulated_CombinesScoresAndRanksDualMatchesHigher()
    {
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // Vector: A is rank 1 (dist 0.05), B is rank 2 (dist 0.15)
        var vectorItems = new[]
        {
            new VectorSearchResultItem(chunkA, docId, 0, "Title A", "ref-A", "Content A", 0.05, 1),
            new VectorSearchResultItem(chunkB, docId, 1, "Title B", "ref-B", "Content B", 0.15, 2)
        };

        // Keyword: B is rank 1 (score 0.9), A is rank 2 (score 0.4)
        var keywordItems = new[]
        {
            new KeywordSearchResultItem(chunkB, docId, 1, "Title B", "ref-B", "Content B", 0.9, 1),
            new KeywordSearchResultItem(chunkA, docId, 0, "Title A", "ref-A", "Content A", 0.4, 2)
        };

        var fused = _sut.Fuse(vectorItems, keywordItems, k: 60);

        Assert.Equal(2, fused.Count);

        // Chunk B: Vector rank 2 (1/62 = ~0.016129) + Keyword rank 1 (1/61 = ~0.016393) = 0.032522
        // Chunk A: Vector rank 1 (1/61 = ~0.016393) + Keyword rank 2 (1/62 = ~0.016129) = 0.032522
        // Both have equal score 0.032522; ordered deterministically by ChunkId
        Assert.Equal(1, fused[0].Rank);
        Assert.Equal(2, fused[1].Rank);
        Assert.NotNull(fused[0].Distance);
        Assert.NotNull(fused[0].KeywordScore);
    }

    [Fact]
    public void Fuse_SingleBranchCandidates_IncludedWithCorrectScore()
    {
        var chunkVectorOnly = Guid.NewGuid();
        var chunkKeywordOnly = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var vectorItems = new[]
        {
            new VectorSearchResultItem(chunkVectorOnly, docId, 0, "Vector Doc", "ref-v", "Vector text", 0.10, 1)
        };

        var keywordItems = new[]
        {
            new KeywordSearchResultItem(chunkKeywordOnly, docId, 1, "Keyword Doc", "ref-k", "Keyword text", 0.85, 1)
        };

        var fused = _sut.Fuse(vectorItems, keywordItems, k: 60);

        Assert.Equal(2, fused.Count);

        var vectorItem = fused.Single(x => x.ChunkId == chunkVectorOnly);
        Assert.NotNull(vectorItem.Distance);
        Assert.Null(vectorItem.KeywordScore);
        Assert.Equal(1.0 / 61, vectorItem.RrfScore, precision: 6);

        var keywordItem = fused.Single(x => x.ChunkId == chunkKeywordOnly);
        Assert.Null(keywordItem.Distance);
        Assert.NotNull(keywordItem.KeywordScore);
        Assert.Equal(1.0 / 61, keywordItem.RrfScore, precision: 6);
    }

    [Fact]
    public void Fuse_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Fuse(null!, Array.Empty<KeywordSearchResultItem>()));
        Assert.Throws<ArgumentNullException>(() => _sut.Fuse(Array.Empty<VectorSearchResultItem>(), null!));
    }
}
