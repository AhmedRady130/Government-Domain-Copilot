using GovernmentDomainCopilot.Application.Documents.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.Tests.Embeddings;

public sealed class ChunkEmbeddingServiceTests
{
    private readonly EmbeddingProviderOptions _options = new() { ExpectedDimensions = 768 };

    [Fact]
    public async Task EmbedAndPersistChunksAsync_HappyPath_GeneratesAndPersistsEmbeddings()
    {
        var tenantId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenantId, docId, 0, "Content 1");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenantId, docId, 1, "Content 2");
        var chunks = new[] { chunk1, chunk2 };

        var vector1 = Enumerable.Repeat(0.1f, 768).ToArray();
        var vector2 = Enumerable.Repeat(0.2f, 768).ToArray();

        var fakeEmbeddingService = new FakeEmbeddingService(new EmbeddingResult(
            providerName: "Gemini",
            modelName: "gemini-embedding-2",
            dimension: 768,
            items: new[]
            {
                new EmbeddingItem(0, vector1),
                new EmbeddingItem(1, vector2)
            },
            duration: TimeSpan.FromMilliseconds(100)));

        var fakeRepository = new FakeChunkEmbeddingRepository();

        var sut = new ChunkEmbeddingService(
            fakeEmbeddingService,
            fakeRepository,
            Options.Create(_options),
            NullLogger<ChunkEmbeddingService>.Instance);

        await sut.EmbedAndPersistChunksAsync(tenantId, chunks, CancellationToken.None);

        Assert.NotNull(fakeEmbeddingService.LastRequest);
        Assert.Equal(tenantId, fakeEmbeddingService.LastRequest.TenantId);
        Assert.Equal(2, fakeEmbeddingService.LastRequest.Inputs.Count);

        Assert.Equal(tenantId, fakeRepository.LastTenantId);
        Assert.NotNull(fakeRepository.LastEmbeddings);
        Assert.Equal(2, fakeRepository.LastEmbeddings.Count);
        Assert.Equal(chunk1.Id, fakeRepository.LastEmbeddings[0].ChunkId);
        Assert.Equal(vector1, fakeRepository.LastEmbeddings[0].Vector);
        Assert.Equal(chunk2.Id, fakeRepository.LastEmbeddings[1].ChunkId);
        Assert.Equal(vector2, fakeRepository.LastEmbeddings[1].Vector);
        Assert.Equal(768, fakeRepository.LastExpectedDimension);
    }

    [Fact]
    public async Task EmbedAndPersistChunksAsync_EmptyChunks_ThrowsArgumentException()
    {
        var sut = CreateSut(new FakeEmbeddingService(), new FakeChunkEmbeddingRepository());
        var tenantId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.EmbedAndPersistChunksAsync(tenantId, Array.Empty<DocumentChunk>()));
    }

    [Fact]
    public async Task EmbedAndPersistChunksAsync_EmptyTenantId_ThrowsArgumentOutOfRangeException()
    {
        var sut = CreateSut(new FakeEmbeddingService(), new FakeChunkEmbeddingRepository());
        var docId = Guid.NewGuid();
        var chunk = new DocumentChunk(Guid.NewGuid(), Guid.NewGuid(), docId, 0, "Content");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.EmbedAndPersistChunksAsync(Guid.Empty, new[] { chunk }));
    }

    [Fact]
    public async Task EmbedAndPersistChunksAsync_CrossTenantChunk_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(new FakeEmbeddingService(), new FakeChunkEmbeddingRepository());
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenant1, docId, 0, "Content 1");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenant2, docId, 1, "Content 2");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.EmbedAndPersistChunksAsync(tenant1, new[] { chunk1, chunk2 }));
    }

    [Fact]
    public async Task EmbedAndPersistChunksAsync_DimensionMismatch_ThrowsInvalidOperationException()
    {
        var badResult = new EmbeddingResult(
            providerName: "Gemini",
            modelName: "gemini-embedding-2",
            dimension: 1536,
            items: new[] { new EmbeddingItem(0, new float[1536]) },
            duration: TimeSpan.FromMilliseconds(50));

        var sut = CreateSut(new FakeEmbeddingService(badResult), new FakeChunkEmbeddingRepository());
        var tenantId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var chunk = new DocumentChunk(Guid.NewGuid(), tenantId, docId, 0, "Content 1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.EmbedAndPersistChunksAsync(tenantId, new[] { chunk }));
    }

    private ChunkEmbeddingService CreateSut(
        IEmbeddingService embeddingService,
        IChunkEmbeddingRepository repository)
    {
        return new ChunkEmbeddingService(
            embeddingService,
            repository,
            Options.Create(_options),
            NullLogger<ChunkEmbeddingService>.Instance);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        private readonly EmbeddingResult? _resultToReturn;

        public FakeEmbeddingService(EmbeddingResult? resultToReturn = null)
        {
            _resultToReturn = resultToReturn;
        }

        public EmbeddingRequest? LastRequest { get; private set; }

        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (_resultToReturn != null)
            {
                return Task.FromResult(_resultToReturn);
            }

            var items = request.Inputs.Select((_, idx) =>
                new EmbeddingItem(idx, Enumerable.Repeat(0.1f, 768).ToList())
            ).ToList();

            return Task.FromResult(new EmbeddingResult("Fake", "fake-model", 768, items, TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class FakeChunkEmbeddingRepository : IChunkEmbeddingRepository
    {
        public Guid LastTenantId { get; private set; }
        public IReadOnlyList<(Guid ChunkId, float[] Vector)>? LastEmbeddings { get; private set; }
        public int LastExpectedDimension { get; private set; }

        public Task PersistEmbeddingsAsync(
            Guid tenantId,
            IReadOnlyList<(Guid ChunkId, float[] Vector)> embeddings,
            int expectedDimension,
            CancellationToken cancellationToken)
        {
            LastTenantId = tenantId;
            LastEmbeddings = embeddings;
            LastExpectedDimension = expectedDimension;
            return Task.CompletedTask;
        }
    }
}
