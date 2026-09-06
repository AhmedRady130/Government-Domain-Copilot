using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Tests.Retrieval;

public sealed class PgVectorChunkRetrieverIntegrationTests : IClassFixture<PgvectorTestDatabaseFixture>
{
    private readonly PgvectorTestDatabaseFixture _fixture;

    public PgVectorChunkRetrieverIntegrationTests(PgvectorTestDatabaseFixture fixture)
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
    public async Task VectorSimilarity_ReturnsNearestChunkFirst_AndMetadataCorrect()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var docId = Guid.NewGuid();
        var document = new Document(docId, tenant.Id, "Procurement Regulation", "ref-proc-101", DateTimeOffset.UtcNow);

        var chunkClose = new DocumentChunk(Guid.NewGuid(), tenant.Id, docId, 0, "Closest Chunk Content");
        var chunkMid = new DocumentChunk(Guid.NewGuid(), tenant.Id, docId, 1, "Middle Chunk Content");
        var chunkFar = new DocumentChunk(Guid.NewGuid(), tenant.Id, docId, 2, "Farthest Chunk Content");

        await repo.SaveAsync(document, new[] { chunkClose, chunkMid, chunkFar }, CancellationToken.None);

        var closeVector = CreateVector(0.1f);
        var midVector = new float[768];
        for (int i = 0; i < 768; i++)
        {
            midVector[i] = i < 384 ? 0.1f : -0.1f;
        }
        var farVector = CreateVector(-0.1f);

        await repo.PersistEmbeddingsAsync(tenant.Id, new[]
        {
            (chunkClose.Id, closeVector),
            (chunkMid.Id, midVector),
            (chunkFar.Id, farVector)
        }, 768, CancellationToken.None);

        var queryVector = CreateVector(0.1f);

        var results = await retriever.SearchVectorAsync(tenant.Id, queryVector, topK: 3, CancellationToken.None);

        Assert.Equal(3, results.Count);

        // Nearest chunk should be Rank 1
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(chunkClose.Id, results[0].ChunkId);
        Assert.Equal(docId, results[0].DocumentId);
        Assert.Equal(0, results[0].Sequence);
        Assert.Equal("Procurement Regulation", results[0].Title);
        Assert.Equal("ref-proc-101", results[0].SourceReference);
        Assert.Equal("Closest Chunk Content", results[0].Content);

        // Verify distance ascending order
        Assert.True(results[0].Distance < results[1].Distance);
        Assert.True(results[1].Distance < results[2].Distance);
    }

    [Fact]
    public async Task TopKLimit_RestrictsReturnedResultsCount()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "TopK Test Doc", "ref-topk-1", DateTimeOffset.UtcNow);
        var chunks = Enumerable.Range(0, 5)
            .Select(i => new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, i, $"Chunk {i}"))
            .ToList();

        await repo.SaveAsync(document, chunks, CancellationToken.None);

        var embeddings = chunks.Select((c, i) => (c.Id, CreateVector(0.1f * (i + 1)))).ToList();
        await repo.PersistEmbeddingsAsync(tenant.Id, embeddings, 768, CancellationToken.None);

        var results = await retriever.SearchVectorAsync(tenant.Id, CreateVector(0.1f), topK: 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(2, results[1].Rank);
    }

    [Fact]
    public async Task NullEmbeddingChunks_AreExcludedFromSearchResults()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Null Embedding Test", "ref-null-search", DateTimeOffset.UtcNow);
        var chunkEmbedded = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Embedded Content");
        var chunkUnembedded = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 1, "Unembedded Content");

        await repo.SaveAsync(document, new[] { chunkEmbedded, chunkUnembedded }, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunkEmbedded.Id, CreateVector(0.2f)) }, 768, CancellationToken.None);

        var results = await retriever.SearchVectorAsync(tenant.Id, CreateVector(0.2f), topK: 10, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(chunkEmbedded.Id, results[0].ChunkId);
        Assert.DoesNotContain(results, r => r.ChunkId == chunkUnembedded.Id);
    }

    [Fact]
    public async Task TenantIsolation_EnforcedAtDatabaseQueryLevel()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");

        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var sharedRef = "ref-shared-query";
        var sharedVector = CreateVector(0.33f);

        // Tenant A Document & Chunk
        var docA = new Document(Guid.NewGuid(), tenantA.Id, "Tenant A Decree", sharedRef, DateTimeOffset.UtcNow);
        var chunkA = new DocumentChunk(Guid.NewGuid(), tenantA.Id, docA.Id, 0, "Secret A Content");
        await repo.SaveAsync(docA, new[] { chunkA }, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenantA.Id, new[] { (chunkA.Id, sharedVector) }, 768, CancellationToken.None);

        // Tenant B Document & Chunk (identical text and vector)
        var docB = new Document(Guid.NewGuid(), tenantB.Id, "Tenant B Decree", sharedRef, DateTimeOffset.UtcNow);
        var chunkB = new DocumentChunk(Guid.NewGuid(), tenantB.Id, docB.Id, 0, "Secret B Content");
        await repo.SaveAsync(docB, new[] { chunkB }, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenantB.Id, new[] { (chunkB.Id, sharedVector) }, 768, CancellationToken.None);

        // Search with Tenant A context
        var resultsA = await retriever.SearchVectorAsync(tenantA.Id, sharedVector, topK: 10, CancellationToken.None);

        Assert.Single(resultsA);
        Assert.Equal(chunkA.Id, resultsA[0].ChunkId);
        Assert.Equal("Secret A Content", resultsA[0].Content);

        // Search with Tenant B context
        var resultsB = await retriever.SearchVectorAsync(tenantB.Id, sharedVector, topK: 10, CancellationToken.None);

        Assert.Single(resultsB);
        Assert.Equal(chunkB.Id, resultsB[0].ChunkId);
        Assert.Equal("Secret B Content", resultsB[0].Content);

        // Cross-tenant check: Tenant B query never returns Tenant A chunk
        Assert.DoesNotContain(resultsB, r => r.ChunkId == chunkA.Id);
        Assert.DoesNotContain(resultsA, r => r.ChunkId == chunkB.Id);
    }

    [Fact]
    public async Task ResultOrdering_DeterministicForEqualDistances()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Equal Distance Doc", "ref-eq-dist", DateTimeOffset.UtcNow);
        var chunk1 = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Chunk 1 Content");
        var chunk2 = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 1, "Chunk 2 Content");

        await repo.SaveAsync(document, new[] { chunk1, chunk2 }, CancellationToken.None);

        var identicalVector = CreateVector(0.5f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk1.Id, identicalVector), (chunk2.Id, identicalVector) }, 768, CancellationToken.None);

        var results = await retriever.SearchVectorAsync(tenant.Id, identicalVector, topK: 5, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(2, results[1].Rank);
    }

    [Fact]
    public async Task VectorDimension768_QueriesSuccessfully()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);
        var retriever = new PgVectorChunkRetriever(context);

        var doc = new Document(Guid.NewGuid(), tenant.Id, "768 Query Doc", "ref-768-query", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, doc.Id, 0, "768 Content");
        await repo.SaveAsync(doc, new[] { chunk }, CancellationToken.None);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, CreateVector(0.42f)) }, 768, CancellationToken.None);

        var results = await retriever.SearchVectorAsync(tenant.Id, CreateVector(0.42f), topK: 1, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].ChunkId);
    }

    [Fact]
    public async Task WrongVectorDimension_FailsSafely()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var retriever = new PgVectorChunkRetriever(context);

        var wrongDimensionVector = new float[128]; // Incorrect 128 dimensions instead of 768

        await Assert.ThrowsAnyAsync<Exception>(() =>
            retriever.SearchVectorAsync(tenant.Id, wrongDimensionVector, topK: 5, CancellationToken.None));
    }
}
