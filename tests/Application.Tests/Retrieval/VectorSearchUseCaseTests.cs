using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Retrieval;

public sealed class VectorSearchUseCaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task SearchAsync_NullRequest_ThrowsVectorSearchValidationException()
    {
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeEmbeddingService(), new FakeChunkRetriever());

        await Assert.ThrowsAsync<VectorSearchValidationException>(() =>
            sut.SearchAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_EmptyOrWhitespaceQuery_ThrowsVectorSearchValidationException(string query)
    {
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeEmbeddingService(), new FakeChunkRetriever());
        var request = new VectorSearchRequest(query);

        await Assert.ThrowsAsync<VectorSearchValidationException>(() =>
            sut.SearchAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_InvalidTopK_ThrowsVectorSearchValidationException(int topK)
    {
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeEmbeddingService(), new FakeChunkRetriever());
        var request = new VectorSearchRequest("valid query", topK);

        await Assert.ThrowsAsync<VectorSearchValidationException>(() =>
            sut.SearchAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_TopKExceedsMax_CapsToMaxTopK()
    {
        var fakeRetriever = new FakeChunkRetriever();
        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeEmbeddingService(), fakeRetriever);
        var request = new VectorSearchRequest("valid query", topK: 100);

        var response = await sut.SearchAsync(request, CancellationToken.None);

        Assert.Equal(VectorSearchLimits.MaxTopK, response.TopK);
        Assert.Equal(VectorSearchLimits.MaxTopK, fakeRetriever.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_ResolvesTenantFromContext()
    {
        var fakeRetriever = new FakeChunkRetriever();
        var fakeEmbeddingService = new FakeEmbeddingService();
        var sut = CreateSut(new FakeTenantContext(_tenantId), fakeEmbeddingService, fakeRetriever);
        var request = new VectorSearchRequest("government procurement rules");

        await sut.SearchAsync(request, CancellationToken.None);

        Assert.Equal(_tenantId, fakeEmbeddingService.LastRequest?.TenantId);
        Assert.Equal(_tenantId, fakeRetriever.LastTenantId);
    }

    [Fact]
    public async Task SearchAsync_CallsEmbeddingServiceOnce_WithQuery()
    {
        var fakeEmbeddingService = new FakeEmbeddingService();
        var sut = CreateSut(new FakeTenantContext(_tenantId), fakeEmbeddingService, new FakeChunkRetriever());
        var request = new VectorSearchRequest("public tenders");

        await sut.SearchAsync(request, CancellationToken.None);

        Assert.Equal(1, fakeEmbeddingService.CallCount);
        Assert.NotNull(fakeEmbeddingService.LastRequest);
        Assert.Single(fakeEmbeddingService.LastRequest.Inputs);
        Assert.Equal("public tenders", fakeEmbeddingService.LastRequest.Inputs[0]);
    }

    [Fact]
    public async Task SearchAsync_DimensionMismatch_ThrowsVectorSearchException()
    {
        var badResult = new EmbeddingResult(
            providerName: "Gemini",
            modelName: "gemini-embedding-2",
            dimension: 128,
            items: new[] { new EmbeddingItem(0, new float[128]) },
            duration: TimeSpan.FromMilliseconds(20));

        var sut = CreateSut(new FakeTenantContext(_tenantId), new FakeEmbeddingService(badResult), new FakeChunkRetriever());
        var request = new VectorSearchRequest("valid query");

        var exception = await Assert.ThrowsAsync<VectorSearchException>(() =>
            sut.SearchAsync(request, CancellationToken.None));

        Assert.Contains("dimension 128 does not match expected dimension 768", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_HappyPath_ReturnsOrderedRankedItems()
    {
        var chunkId1 = Guid.NewGuid();
        var chunkId2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var returnedItems = new[]
        {
            new VectorSearchResultItem(chunkId1, docId, 0, "Title 1", "ref-1", "Content 1", distance: 0.12, rank: 1),
            new VectorSearchResultItem(chunkId2, docId, 1, "Title 1", "ref-1", "Content 2", distance: 0.35, rank: 2)
        };

        var fakeRetriever = new FakeChunkRetriever(returnedItems);
        var fakeEmbedding = new FakeEmbeddingService();
        var sut = CreateSut(new FakeTenantContext(_tenantId), fakeEmbedding, fakeRetriever);

        var request = new VectorSearchRequest("procurement decree", topK: 5);

        var response = await sut.SearchAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(5, response.TopK);
        Assert.Equal(2, response.TotalReturned);
        Assert.Equal("Fake", response.ProviderName);
        Assert.Equal("fake-model", response.ModelName);
        Assert.Equal(2, response.Items.Count);

        Assert.Equal(1, response.Items[0].Rank);
        Assert.Equal(chunkId1, response.Items[0].ChunkId);
        Assert.Equal(0.12, response.Items[0].Distance);

        Assert.Equal(2, response.Items[1].Rank);
        Assert.Equal(chunkId2, response.Items[1].ChunkId);
        Assert.Equal(0.35, response.Items[1].Distance);
    }

    private static VectorSearchUseCase CreateSut(
        ITenantContext tenantContext,
        IEmbeddingService embeddingService,
        IChunkRetriever chunkRetriever)
    {
        return new VectorSearchUseCase(
            tenantContext,
            embeddingService,
            chunkRetriever,
            NullLogger<VectorSearchUseCase>.Instance);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        public FakeTenantContext(Guid tenantId)
        {
            _tenantId = tenantId;
        }

        public Guid GetTenantId() => _tenantId;
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        private readonly EmbeddingResult? _result;

        public FakeEmbeddingService(EmbeddingResult? result = null)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public EmbeddingRequest? LastRequest { get; private set; }

        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;

            if (_result != null)
            {
                return Task.FromResult(_result);
            }

            var items = request.Inputs.Select((_, idx) =>
                new EmbeddingItem(idx, Enumerable.Repeat(0.05f, 768).ToList())
            ).ToList();

            return Task.FromResult(new EmbeddingResult("Fake", "fake-model", 768, items, TimeSpan.FromMilliseconds(15)));
        }
    }

    private sealed class FakeChunkRetriever : IChunkRetriever
    {
        private readonly IReadOnlyList<VectorSearchResultItem>? _resultsToReturn;

        public FakeChunkRetriever(IReadOnlyList<VectorSearchResultItem>? resultsToReturn = null)
        {
            _resultsToReturn = resultsToReturn;
        }

        public Guid LastTenantId { get; private set; }
        public float[]? LastQueryVector { get; private set; }
        public int LastTopK { get; private set; }

        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId,
            float[] queryVector,
            int topK,
            CancellationToken cancellationToken)
        {
            LastTenantId = tenantId;
            LastQueryVector = queryVector;
            LastTopK = topK;

            return Task.FromResult(_resultsToReturn ?? Array.Empty<VectorSearchResultItem>());
        }
    }
}
