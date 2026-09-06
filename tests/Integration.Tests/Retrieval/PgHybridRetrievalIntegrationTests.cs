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

    private HybridSearchUseCase CreateSut(
        GovernmentDomainCopilotDbContext context,
        Guid tenantId,
        float[] queryVector)
    {
        var vectorRetriever = new PgVectorChunkRetriever(context);
        var keywordRetriever = new PgKeywordChunkRetriever(context);
        var fakeEmbeddingService = new StubEmbeddingService(queryVector);
        var vectorUseCase = new VectorSearchUseCase(
            new StubTenantContext(tenantId),
            fakeEmbeddingService,
            vectorRetriever,
            NullLogger<VectorSearchUseCase>.Instance);

        return new HybridSearchUseCase(
            new StubTenantContext(tenantId),
            vectorUseCase,
            keywordRetriever,
            new ReciprocalRankFusionService(),
            new WeightedSignalReranker(),
            NullLogger<HybridSearchUseCase>.Instance);
    }

    [Fact]
    public async Task HybridSearch_DualBranchMatch_RrfBoostsDualMatchHigherThanSingleMatch()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Hybrid Policy", "ref-hyb-101", DateTimeOffset.UtcNow);
        var chunkDual = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "National procurement framework and civil tenders.");
        var chunkVectorOnly = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 1, "Unrelated municipal park maintenance decree.");

        await repo.SaveAsync(doc, new[] { chunkDual, chunkVectorOnly }, CancellationToken.None);

        var closeVector = CreateVector(0.1f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[]
        {
            (chunkDual.Id, closeVector),
            (chunkVectorOnly.Id, closeVector)
        }, 768, CancellationToken.None);

        var sut = CreateSut(context, tenant.Id, closeVector);
        var response = await sut.SearchAsync(new VectorSearchRequest("procurement tenders", topK: 5), CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Items.Count >= 2);
        Assert.Equal(chunkDual.Id, response.Items[0].ChunkId);
        Assert.Equal(1, response.Items[0].FinalRank);
        Assert.NotNull(response.Items[0].Distance);
        Assert.NotNull(response.Items[0].KeywordScore);
    }

    [Fact]
    public async Task HybridSearch_KeywordOnlyMatch_IncludedInFinalRanking()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Unembedded Policy", "ref-hyb-unembedded", DateTimeOffset.UtcNow);
        var chunkUnembedded = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "Special environmental safety ordinance.");
        await repo.SaveAsync(doc, new[] { chunkUnembedded }, CancellationToken.None);

        var sut = CreateSut(context, tenant.Id, CreateVector(0.1f));
        var response = await sut.SearchAsync(new VectorSearchRequest("environmental safety", topK: 5), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(chunkUnembedded.Id, response.Items[0].ChunkId);
        Assert.Null(response.Items[0].Distance);
        Assert.NotNull(response.Items[0].KeywordScore);
        Assert.True(response.Items[0].RerankScore > 0);
    }

    [Fact]
    public async Task HybridSearch_VectorOnlyMatch_IncludedInFinalRanking()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Vector Policy", "ref-vec-only", DateTimeOffset.UtcNow);
        // Text has no keyword matches for "cybersecurity protocol"
        var chunkVectorOnly = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "General IT equipment guidelines.");
        await repo.SaveAsync(doc, new[] { chunkVectorOnly }, CancellationToken.None);

        var matchingVector = CreateVector(0.05f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunkVectorOnly.Id, matchingVector) }, 768, CancellationToken.None);

        var sut = CreateSut(context, tenant.Id, matchingVector);
        var response = await sut.SearchAsync(new VectorSearchRequest("cybersecurity protocol", topK: 5), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(chunkVectorOnly.Id, response.Items[0].ChunkId);
        Assert.NotNull(response.Items[0].Distance);
        Assert.Null(response.Items[0].KeywordScore);
    }

    [Fact]
    public async Task HybridSearch_TenantIsolation_TenantAAndTenantBDoNotInfluenceReranking()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");
        var repo = new DocumentRepository(context);

        var docA = new Document(Guid.NewGuid(), tenantA.Id, "Doc A", "ref-A", DateTimeOffset.UtcNow);
        var chunkA = new DocumentChunk(Guid.NewGuid(), tenantA.Id, docA.Id, 0, "Shared decree content for tenant A.");
        await repo.SaveAsync(docA, new[] { chunkA }, CancellationToken.None);

        var docB = new Document(Guid.NewGuid(), tenantB.Id, "Doc B", "ref-B", DateTimeOffset.UtcNow);
        var chunkB = new DocumentChunk(Guid.NewGuid(), tenantB.Id, docB.Id, 0, "Shared decree content for tenant B.");
        await repo.SaveAsync(docB, new[] { chunkB }, CancellationToken.None);

        var queryVector = CreateVector(0.1f);
        await repo.PersistEmbeddingsAsync(tenantA.Id, new[] { (chunkA.Id, queryVector) }, 768, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenantB.Id, new[] { (chunkB.Id, queryVector) }, 768, CancellationToken.None);

        var sutA = CreateSut(context, tenantA.Id, queryVector);
        var sutB = CreateSut(context, tenantB.Id, queryVector);

        var resA = await sutA.SearchAsync(new VectorSearchRequest("decree content", topK: 10), CancellationToken.None);
        var resB = await sutB.SearchAsync(new VectorSearchRequest("decree content", topK: 10), CancellationToken.None);

        Assert.All(resA.Items, item => Assert.Equal(chunkA.Id, item.ChunkId));
        Assert.All(resB.Items, item => Assert.Equal(chunkB.Id, item.ChunkId));
    }

    [Fact]
    public async Task HybridSearch_RepeatedIdenticalSearches_ProduceIdenticalOrderAndScores()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Doc Stable", "ref-stable", DateTimeOffset.UtcNow);
        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "Decree regulations part 1.");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 1, "Decree regulations part 2.");
        await repo.SaveAsync(doc, new[] { chunk1, chunk2 }, CancellationToken.None);

        var vec = CreateVector(0.1f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk1.Id, vec), (chunk2.Id, vec) }, 768, CancellationToken.None);

        var sut = CreateSut(context, tenant.Id, vec);

        var res1 = await sut.SearchAsync(new VectorSearchRequest("decree regulations", topK: 5), CancellationToken.None);
        var res2 = await sut.SearchAsync(new VectorSearchRequest("decree regulations", topK: 5), CancellationToken.None);

        Assert.Equal(res1.Items.Count, res2.Items.Count);
        for (int i = 0; i < res1.Items.Count; i++)
        {
            Assert.Equal(res1.Items[i].ChunkId, res2.Items[i].ChunkId);
            Assert.Equal(res1.Items[i].FinalRank, res2.Items[i].FinalRank);
            Assert.Equal(res1.Items[i].RerankScore, res2.Items[i].RerankScore);
        }
    }

    [Fact]
    public async Task HybridSearch_FinalResultsRespectRequestedTopK()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Multi Chunk Doc", "ref-multi", DateTimeOffset.UtcNow);
        var chunks = Enumerable.Range(0, 5).Select(i =>
            new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, i, $"Public administration sector section {i}.")).ToList();
        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var vec = CreateVector(0.1f);
        await repo.PersistEmbeddingsAsync(tenant.Id, chunks.Select(c => (c.Id, vec)).ToList(), 768, CancellationToken.None);

        var sut = CreateSut(context, tenant.Id, vec);

        var response = await sut.SearchAsync(new VectorSearchRequest("public administration", topK: 2), CancellationToken.None);

        Assert.Equal(2, response.TopK);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task HybridSearch_AllReturnedChunksBelongToCurrentTenant()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenantTarget = await CreateTenantAsync(context, "Target Tenant");
        var tenantOther = await CreateTenantAsync(context, "Other Tenant");
        var repo = new DocumentRepository(context);

        var docTarget = new Document(Guid.NewGuid(), tenantTarget.Id, "Target Doc", "ref-t", DateTimeOffset.UtcNow);
        var chunkTarget = new DocumentChunk(Guid.NewGuid(), tenantTarget.Id, docTarget.Id, 0, "Target tenant civil directive.");
        await repo.SaveAsync(docTarget, new[] { chunkTarget }, CancellationToken.None);

        var docOther = new Document(Guid.NewGuid(), tenantOther.Id, "Other Doc", "ref-o", DateTimeOffset.UtcNow);
        var chunkOther = new DocumentChunk(Guid.NewGuid(), tenantOther.Id, docOther.Id, 0, "Other tenant civil directive.");
        await repo.SaveAsync(docOther, new[] { chunkOther }, CancellationToken.None);

        var vec = CreateVector(0.1f);
        await repo.PersistEmbeddingsAsync(tenantTarget.Id, new[] { (chunkTarget.Id, vec) }, 768, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenantOther.Id, new[] { (chunkOther.Id, vec) }, 768, CancellationToken.None);

        var sut = CreateSut(context, tenantTarget.Id, vec);
        var response = await sut.SearchAsync(new VectorSearchRequest("civil directive", topK: 10), CancellationToken.None);

        Assert.NotEmpty(response.Items);
        Assert.DoesNotContain(response.Items, item => item.ChunkId == chunkOther.Id);
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
