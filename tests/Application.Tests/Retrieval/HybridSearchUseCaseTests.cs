using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Retrieval;

public sealed class HybridSearchUseCaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task SearchAsync_NullRequest_ThrowsVectorSearchValidationException()
    {
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeVectorSearchUseCase(), new FakeKeywordRetriever());

        await Assert.ThrowsAsync<VectorSearchValidationException>(() =>
            sut.SearchAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_EmptyQuery_ThrowsVectorSearchValidationException(string query)
    {
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeVectorSearchUseCase(), new FakeKeywordRetriever());

        await Assert.ThrowsAsync<VectorSearchValidationException>(() =>
            sut.SearchAsync(new VectorSearchRequest(query), CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_VectorBranchFails_DegradesToKeywordOnly()
    {
        var failingVectorUseCase = new FakeVectorSearchUseCase(shouldThrow: true);
        var fakeKeywordRetriever = new FakeKeywordRetriever(new[]
        {
            new KeywordSearchResultItem(Guid.NewGuid(), Guid.NewGuid(), 0, "Title", "ref", "Content", 0.8, 1)
        });

        var sut = CreateSut(new FakeTenantContext(_tenantId), failingVectorUseCase, fakeKeywordRetriever);

        var response = await sut.SearchAsync(new VectorSearchRequest("procurement decree"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.NotNull(response.Items[0].KeywordScore);
        Assert.Null(response.Items[0].Distance);
    }

    [Fact]
    public async Task SearchAsync_KeywordBranchFails_DegradesToVectorOnly()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var fakeVectorUseCase = new FakeVectorSearchUseCase(new VectorSearchResponse(
            5, 1, TimeSpan.FromMilliseconds(10), "Gemini", "gemini-embedding-2",
            new[] { new VectorSearchResultItem(chunkId, docId, 0, "Title", "ref", "Content", 0.1, 1) }));

        var failingKeywordRetriever = new FakeKeywordRetriever(shouldThrow: true);

        var sut = CreateSut(new FakeTenantContext(_tenantId), fakeVectorUseCase, failingKeywordRetriever);

        var response = await sut.SearchAsync(new VectorSearchRequest("procurement decree"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.NotNull(response.Items[0].Distance);
        Assert.Null(response.Items[0].KeywordScore);
    }

    [Fact]
    public async Task SearchAsync_BothBranchesFail_ThrowsVectorSearchException()
    {
        var failingVectorUseCase = new FakeVectorSearchUseCase(shouldThrow: true);
        var failingKeywordRetriever = new FakeKeywordRetriever(shouldThrow: true);

        var sut = CreateSut(new FakeTenantContext(_tenantId), failingVectorUseCase, failingKeywordRetriever);

        await Assert.ThrowsAsync<VectorSearchException>(() =>
            sut.SearchAsync(new VectorSearchRequest("procurement decree"), CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_HappyPath_FusesResultsAndTruncatesToTopK()
    {
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var vectorResponse = new VectorSearchResponse(
            10, 2, TimeSpan.FromMilliseconds(10), "Gemini", "gemini-embedding-2",
            new[]
            {
                new VectorSearchResultItem(chunkA, docId, 0, "Title A", "ref-A", "Content A", 0.05, 1),
                new VectorSearchResultItem(chunkB, docId, 1, "Title B", "ref-B", "Content B", 0.20, 2)
            });

        var fakeVectorUseCase = new FakeVectorSearchUseCase(vectorResponse);
        var fakeKeywordRetriever = new FakeKeywordRetriever(new[]
        {
            new KeywordSearchResultItem(chunkA, docId, 0, "Title A", "ref-A", "Content A", 0.95, 1)
        });

        var sut = CreateSut(new FakeTenantContext(_tenantId), fakeVectorUseCase, fakeKeywordRetriever);

        var response = await sut.SearchAsync(new VectorSearchRequest("procurement decree", topK: 1), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(1, response.TopK);
        Assert.Single(response.Items);
        Assert.Equal(chunkA, response.Items[0].ChunkId);
        Assert.NotNull(response.Items[0].Distance);
        Assert.NotNull(response.Items[0].KeywordScore);
    }

    private static HybridSearchUseCase CreateSut(
        ITenantContext tenantContext,
        IVectorSearchUseCase vectorSearchUseCase,
        IKeywordChunkRetriever keywordRetriever)
    {
        return new HybridSearchUseCase(
            tenantContext,
            vectorSearchUseCase,
            keywordRetriever,
            new ReciprocalRankFusionService(),
            NullLogger<HybridSearchUseCase>.Instance);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;
        public FakeTenantContext(Guid tenantId) => _tenantId = tenantId;
        public Guid GetTenantId() => _tenantId;
    }

    private sealed class FakeVectorSearchUseCase : IVectorSearchUseCase
    {
        private readonly VectorSearchResponse? _response;
        private readonly bool _shouldThrow;

        public FakeVectorSearchUseCase(VectorSearchResponse? response = null, bool shouldThrow = false)
        {
            _response = response;
            _shouldThrow = shouldThrow;
        }

        public Task<VectorSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken)
        {
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Vector branch failed.");
            }

            return Task.FromResult(_response ?? new VectorSearchResponse(
                request.TopK ?? 5, 0, TimeSpan.FromMilliseconds(5), "Fake", "fake-model", Array.Empty<VectorSearchResultItem>()));
        }
    }

    private sealed class FakeKeywordRetriever : IKeywordChunkRetriever
    {
        private readonly IReadOnlyList<KeywordSearchResultItem>? _results;
        private readonly bool _shouldThrow;

        public FakeKeywordRetriever(IReadOnlyList<KeywordSearchResultItem>? results = null, bool shouldThrow = false)
        {
            _results = results;
            _shouldThrow = shouldThrow;
        }

        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(Guid tenantId, string query, int topK, CancellationToken cancellationToken)
        {
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Keyword branch failed.");
            }

            return Task.FromResult(_results ?? Array.Empty<KeywordSearchResultItem>());
        }
    }
}
