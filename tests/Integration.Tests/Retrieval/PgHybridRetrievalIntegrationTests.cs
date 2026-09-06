using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Integration.Tests.Retrieval;

public sealed class PgHybridRetrievalIntegrationTests : IClassFixture<PgvectorTestDatabaseFixture>
{
    private readonly PgvectorTestDatabaseFixture _fixture;

    public PgHybridRetrievalIntegrationTests(PgvectorTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Tenant> CreateTenantAsync(GovernmentDomainCopilotDbContext context, string? name = null)
    {
        var tenant = new Tenant(Guid.NewGuid(), name ?? "Test Tenant", DateTimeOffset.UtcNow);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static float[] CreateVector(float fillValue)
    {
        var vector = new float[768];
        Array.Fill(vector, fillValue);
        return vector;
    }

    [Fact]
    public async Task HybridSearch_DualBranchMatch_RrfBoostsDualMatchHigherThanSingleMatch()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var vectorRetriever = new PgVectorChunkRetriever(context);
        var keywordRetriever = new PgKeywordChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Hybrid Policy", "ref-hyb-101", DateTimeOffset.UtcNow);

        // Chunk 1: Dual match (has exact keywords AND vector match)
        var chunkDual = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "National procurement framework and civil tenders.");

        // Chunk 2: Vector match only (different text, but vector match)
        var chunkVectorOnly = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 1, "Unrelated municipal park maintenance decree.");

        await repo.SaveAsync(doc, new[] { chunkDual, chunkVectorOnly }, CancellationToken.None);

        var closeVector = CreateVector(0.1f);
        var farVector = CreateVector(-0.1f);

        await repo.PersistEmbeddingsAsync(tenant.Id, new[]
        {
            (chunkDual.Id, closeVector),
            (chunkVectorOnly.Id, closeVector)
        }, 768, CancellationToken.None);

        var fakeEmbeddingService = new StubEmbeddingService(closeVector);
        var vectorUseCase = new VectorSearchUseCase(new StubTenantContext(tenant.Id), fakeEmbeddingService, vectorRetriever, NullLogger<VectorSearchUseCase>.Instance);

        var sut = new HybridSearchUseCase(
            new StubTenantContext(tenant.Id),
            vectorUseCase,
            keywordRetriever,
            new ReciprocalRankFusionService(),
            NullLogger<HybridSearchUseCase>.Instance);

        var response = await sut.SearchAsync(new VectorSearchRequest("procurement tenders", topK: 5), CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Items.Count >= 2);

        // ChunkDual appeared in BOTH vector and keyword branches, so its RRF score is higher!
        Assert.Equal(chunkDual.Id, response.Items[0].ChunkId);
        Assert.Equal(1, response.Items[0].Rank);
        Assert.NotNull(response.Items[0].Distance);
        Assert.NotNull(response.Items[0].KeywordScore);
    }

    [Fact]
    public async Task HybridSearch_KeywordOnlyMatch_IncludedInResults()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var vectorRetriever = new PgVectorChunkRetriever(context);
        var keywordRetriever = new PgKeywordChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Unembedded Policy", "ref-hyb-unembedded", DateTimeOffset.UtcNow);

        // Chunk has NO embedding (null), so vector search ignores it. But keyword search matches it!
        var chunkUnembedded = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "Special environmental safety ordinance.");
        await repo.SaveAsync(doc, new[] { chunkUnembedded }, CancellationToken.None);

        var fakeEmbeddingService = new StubEmbeddingService(CreateVector(0.1f));
        var vectorUseCase = new VectorSearchUseCase(new StubTenantContext(tenant.Id), fakeEmbeddingService, vectorRetriever, NullLogger<VectorSearchUseCase>.Instance);

        var sut = new HybridSearchUseCase(
            new StubTenantContext(tenant.Id),
            vectorUseCase,
            keywordRetriever,
            new ReciprocalRankFusionService(),
            NullLogger<HybridSearchUseCase>.Instance);

        var response = await sut.SearchAsync(new VectorSearchRequest("environmental safety", topK: 5), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(chunkUnembedded.Id, response.Items[0].ChunkId);
        Assert.Null(response.Items[0].Distance);
        Assert.NotNull(response.Items[0].KeywordScore);
    }

    private sealed class StubTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;
        public StubTenantContext(Guid tenantId) => _tenantId = tenantId;
        public Guid GetTenantId() => _tenantId;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        private readonly float[] _vector;

        public StubEmbeddingService(float[] vector)
        {
            _vector = vector;
        }

        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            var items = request.Inputs.Select((_, idx) => new EmbeddingItem(idx, _vector)).ToList();
            return Task.FromResult(new EmbeddingResult("Stub", "stub-model", 768, items, TimeSpan.FromMilliseconds(5)));
        }
    }
}
