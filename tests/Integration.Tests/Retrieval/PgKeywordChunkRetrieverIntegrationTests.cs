using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using Xunit;

namespace Integration.Tests.Retrieval;

public sealed class PgKeywordChunkRetrieverIntegrationTests : IClassFixture<PgvectorTestDatabaseFixture>
{
    private readonly PgvectorTestDatabaseFixture _fixture;

    public PgKeywordChunkRetrieverIntegrationTests(PgvectorTestDatabaseFixture fixture)
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

    [Fact]
    public async Task KeywordSearch_EnglishQuery_ReturnsMatchingChunk()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgKeywordChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Procurement Guidelines", "ref-kw-eng", DateTimeOffset.UtcNow);
        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "Public procurement terms and conditions for civil tenders.");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 1, "Environmental sustainability policies for municipal parks.");

        await repo.SaveAsync(doc, new[] { chunk1, chunk2 }, CancellationToken.None);

        var results = await retriever.SearchKeywordAsync(tenant.Id, "procurement tenders", topK: 5, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(chunk1.Id, results[0].ChunkId);
        Assert.Equal("Procurement Guidelines", results[0].Title);
        Assert.Equal(1, results[0].Rank);
        Assert.True(results[0].KeywordScore > 0);
    }

    [Fact]
    public async Task KeywordSearch_ArabicQuery_ReturnsMatchingChunk()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgKeywordChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "مرسوم المناقصات", "ref-kw-arb", DateTimeOffset.UtcNow);
        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "لائحة الشراء الحكومي والعقود العامة للدولة");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 1, "قواعد حماية البيئة والتنمية المستدامة");

        await repo.SaveAsync(doc, new[] { chunk1, chunk2 }, CancellationToken.None);

        var results = await retriever.SearchKeywordAsync(tenant.Id, "الشراء الحكومي", topK: 5, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(chunk1.Id, results[0].ChunkId);
        Assert.Equal("مرسوم المناقصات", results[0].Title);
    }

    [Fact]
    public async Task KeywordSearch_TenantIsolation_EnforcedAtDatabaseLevel()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");
        var repo = new DocumentRepository(context);
        var retriever = new PgKeywordChunkRetriever(context);

        var docA = new Document(Guid.NewGuid(), tenantA.Id, "Secret Decree A", "ref-kw-iso-a", DateTimeOffset.UtcNow);
        var chunkA = new DocumentChunk(Guid.NewGuid(), tenantA.Id, docA.Id, 0, "Confidential financial auditing regulations.");
        await repo.SaveAsync(docA, new[] { chunkA }, CancellationToken.None);

        var docB = new Document(Guid.NewGuid(), tenantB.Id, "Secret Decree B", "ref-kw-iso-b", DateTimeOffset.UtcNow);
        var chunkB = new DocumentChunk(Guid.NewGuid(), tenantB.Id, docB.Id, 0, "Confidential financial auditing regulations.");
        await repo.SaveAsync(docB, new[] { chunkB }, CancellationToken.None);

        var resultsA = await retriever.SearchKeywordAsync(tenantA.Id, "financial auditing", topK: 5, CancellationToken.None);
        var resultsB = await retriever.SearchKeywordAsync(tenantB.Id, "financial auditing", topK: 5, CancellationToken.None);

        Assert.Single(resultsA);
        Assert.Equal(chunkA.Id, resultsA[0].ChunkId);

        Assert.Single(resultsB);
        Assert.Equal(chunkB.Id, resultsB[0].ChunkId);

        Assert.DoesNotContain(resultsA, r => r.ChunkId == chunkB.Id);
        Assert.DoesNotContain(resultsB, r => r.ChunkId == chunkA.Id);
    }

    [Fact]
    public async Task KeywordSearch_UnembeddedChunks_ParticipateInKeywordSearch()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgKeywordChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "Unembedded Doc", "ref-kw-unembedded", DateTimeOffset.UtcNow);
        var chunkWithoutEmbedding = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "Emergency medical protocol for urban transport.");

        await repo.SaveAsync(doc, new[] { chunkWithoutEmbedding }, CancellationToken.None);

        // Chunk has null embedding but should still match keyword query
        var results = await retriever.SearchKeywordAsync(tenant.Id, "medical protocol", topK: 5, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(chunkWithoutEmbedding.Id, results[0].ChunkId);
    }
}
