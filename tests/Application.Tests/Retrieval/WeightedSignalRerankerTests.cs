using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using Xunit;

namespace Application.Tests.Retrieval;

public sealed class WeightedSignalRerankerTests
{
    private readonly WeightedSignalReranker _sut = new();

    [Fact]
    public void Rerank_NullRequest_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Rerank(null!));
    }

    [Fact]
    public void Rerank_EmptyCandidates_ReturnsEmptyList()
    {
        var request = new RerankRequest(Array.Empty<HybridSearchResultItem>());
        var result = _sut.Rerank(request);
        Assert.Empty(result);
    }

    [Fact]
    public void Rerank_IsDeterministic_ProducesIdenticalResultsForIdenticalInput()
    {
        var chunk1 = Guid.NewGuid();
        var chunk2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var candidates = new List<HybridSearchResultItem>
        {
            new(chunk1, docId, 0, "Doc A", "ref-A", "Content A", 0.2, 0.9, 0.032, 1),
            new(chunk2, docId, 1, "Doc B", "ref-B", "Content B", 0.5, 0.4, 0.016, 2)
        };

        var request = new RerankRequest(candidates);

        var run1 = _sut.Rerank(request);
        var run2 = _sut.Rerank(request);

        Assert.Equal(run1.Count, run2.Count);
        for (int i = 0; i < run1.Count; i++)
        {
            Assert.Equal(run1[i].ChunkId, run2[i].ChunkId);
            Assert.Equal(run1[i].RerankScore, run2[i].RerankScore);
            Assert.Equal(run1[i].FinalRank, run2[i].FinalRank);
        }
    }

    [Fact]
    public void Rerank_ScoreNormalizationAndClampingCorrectness()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // Max RRF = 0.032, Max Keyword = 1.0, Distance = 0.0 (VectorCloseness = 1.0)
        // NormRrf = 0.032 / 0.032 = 1.0
        // VectorCloseness = 1.0 - 0.0/2.0 = 1.0
        // NormKeyword = 1.0 / 1.0 = 1.0
        // Expected RerankScore = 0.50 * 1.0 + 0.30 * 1.0 + 0.20 * 1.0 = 1.00
        var candidates = new[]
        {
            new HybridSearchResultItem(chunkId, docId, 0, "Perfect", "ref", "Content", 0.0, 1.0, 0.032, 1)
        };

        var result = _sut.Rerank(new RerankRequest(candidates));

        Assert.Single(result);
        Assert.Equal(1.0, result[0].RerankScore, precision: 4);
        Assert.Equal(1, result[0].FinalRank);
    }

    [Fact]
    public void Rerank_WeightedScoreCalculation_MatchesFormula()
    {
        var chunk1 = Guid.NewGuid();
        var chunk2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // c1: Rrf=0.032 (max), Distance=0.4 (closeness = 0.8), Keyword=1.0 (max)
        // RerankScore c1 = 0.50 * 1.0 + 0.30 * 0.8 + 0.20 * 1.0 = 0.50 + 0.24 + 0.20 = 0.94
        // c2: Rrf=0.016 (norm = 0.5), Distance=1.0 (closeness = 0.5), Keyword=0.5 (norm = 0.5)
        // RerankScore c2 = 0.50 * 0.5 + 0.30 * 0.5 + 0.20 * 0.5 = 0.25 + 0.15 + 0.10 = 0.50
        var candidates = new[]
        {
            new HybridSearchResultItem(chunk1, docId, 0, "Doc 1", "ref1", "Text 1", 0.4, 1.0, 0.032, 1),
            new HybridSearchResultItem(chunk2, docId, 1, "Doc 2", "ref2", "Text 2", 1.0, 0.5, 0.016, 2)
        };

        var result = _sut.Rerank(new RerankRequest(candidates));

        Assert.Equal(2, result.Count);
        Assert.Equal(chunk1, result[0].ChunkId);
        Assert.Equal(0.94, result[0].RerankScore, precision: 4);
        Assert.Equal(1, result[0].FinalRank);

        Assert.Equal(chunk2, result[1].ChunkId);
        Assert.Equal(0.50, result[1].RerankScore, precision: 4);
        Assert.Equal(2, result[1].FinalRank);
    }

    [Fact]
    public void Rerank_MissingVectorSignal_HandledAsZeroVectorCloseness()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // Distance is null (keyword-only chunk) -> VectorCloseness = 0.0
        // Rrf = 0.016 (max), Keyword = 0.5 (max)
        // RerankScore = 0.50 * 1.0 + 0.30 * 0.0 + 0.20 * 1.0 = 0.70
        var candidates = new[]
        {
            new HybridSearchResultItem(chunkId, docId, 0, "Keyword Only", "ref", "Content", null, 0.5, 0.016, 1)
        };

        var result = _sut.Rerank(new RerankRequest(candidates));

        Assert.Single(result);
        Assert.Null(result[0].Distance);
        Assert.Equal(0.70, result[0].RerankScore, precision: 4);
    }

    [Fact]
    public void Rerank_MissingKeywordSignal_HandledAsZeroNormKeyword()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // KeywordScore is null (vector-only chunk) -> NormKeywordScore = 0.0
        // Rrf = 0.016 (max), Distance = 0.0 (closeness = 1.0)
        // RerankScore = 0.50 * 1.0 + 0.30 * 1.0 + 0.20 * 0.0 = 0.80
        var candidates = new[]
        {
            new HybridSearchResultItem(chunkId, docId, 0, "Vector Only", "ref", "Content", 0.0, null, 0.016, 1)
        };

        var result = _sut.Rerank(new RerankRequest(candidates));

        Assert.Single(result);
        Assert.Null(result[0].KeywordScore);
        Assert.Equal(0.80, result[0].RerankScore, precision: 4);
    }

    [Fact]
    public void Rerank_TieBreaking_IsDeterministicAcrossFourTierRules()
    {
        // Test tie breaking:
        // Rule 1: RerankScore DESC
        // Rule 2: RrfScore DESC
        // Rule 3: Rank (pre-rerank) ASC
        // Rule 4: ChunkId ASC
        var chunkA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var chunkB = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var docId = Guid.NewGuid();

        // Both chunks produce exact same RerankScore and exact same RrfScore, but chunkA has Rank=1 vs chunkB Rank=2
        var candidates = new[]
        {
            new HybridSearchResultItem(chunkB, docId, 1, "B", "refB", "Content B", 0.5, 0.5, 0.016, 2),
            new HybridSearchResultItem(chunkA, docId, 0, "A", "refA", "Content A", 0.5, 0.5, 0.016, 1)
        };

        var result = _sut.Rerank(new RerankRequest(candidates));

        Assert.Equal(2, result.Count);
        Assert.Equal(chunkA, result[0].ChunkId); // Rank 1 comes before Rank 2
        Assert.Equal(chunkB, result[1].ChunkId);

        // Lexicographical Guid tie breaking test (when RerankScore, RrfScore, AND Rank are identical)
        var candidatesIdenticalRank = new[]
        {
            new HybridSearchResultItem(chunkB, docId, 1, "B", "refB", "Content B", 0.5, 0.5, 0.016, 1),
            new HybridSearchResultItem(chunkA, docId, 0, "A", "refA", "Content A", 0.5, 0.5, 0.016, 1)
        };

        var resultGuidTie = _sut.Rerank(new RerankRequest(candidatesIdenticalRank));

        Assert.Equal(chunkA, resultGuidTie[0].ChunkId); // 0000...01 < 0000...02
        Assert.Equal(chunkB, resultGuidTie[1].ChunkId);
    }

    [Fact]
    public void Rerank_DoesNotMutateCandidateIdentityOrMetadata()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var candidate = new HybridSearchResultItem(
            chunkId, docId, 42, "Original Title", "ref-original", "Original Content", 0.25, 0.75, 0.032, 3);

        var result = _sut.Rerank(new RerankRequest(new[] { candidate }));

        Assert.Single(result);
        var item = result[0];

        Assert.Equal(chunkId, item.ChunkId);
        Assert.Equal(docId, item.DocumentId);
        Assert.Equal(42, item.Sequence);
        Assert.Equal("Original Title", item.Title);
        Assert.Equal("ref-original", item.SourceReference);
        Assert.Equal("Original Content", item.Content);
        Assert.Equal(0.25, item.Distance);
        Assert.Equal(0.75, item.KeywordScore);
        Assert.Equal(0.032, item.RrfScore);
        Assert.Equal(3, item.Rank); // Preserves original fusion rank
        Assert.Equal(1, item.FinalRank); // New 1-based final rank
    }
}
