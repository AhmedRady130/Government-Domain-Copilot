using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Tests.Embeddings;

public sealed class ChunkEmbeddingPersistenceTests : IClassFixture<PgvectorTestDatabaseFixture>
{
    private readonly PgvectorTestDatabaseFixture _fixture;

    public ChunkEmbeddingPersistenceTests(PgvectorTestDatabaseFixture fixture)
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
    public async Task PgvectorExtension_ExistsAfterMigration()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var extensions = await context.Database
            .SqlQueryRaw<string>("SELECT extname FROM pg_extension WHERE extname = 'vector'")
            .ToListAsync();

        Assert.Single(extensions);
        Assert.Equal("vector", extensions[0]);
    }

    [Fact]
    public async Task ChunkEmbedding_PersistsAsVector()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Test Doc", "ref-persist-1", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Vector Test Content");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var vector = Enumerable.Range(1, 768).Select(i => (float)i / 1000f).ToArray();
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, vector) }, 768, CancellationToken.None);

        // Fetch back in new DbContext
        await using var readContext = _fixture.CreateDbContext();
        var readChunk = await readContext.DocumentChunks
            .FirstOrDefaultAsync(c => c.Id == chunk.Id && c.TenantId == tenant.Id);

        Assert.NotNull(readChunk);
        Assert.NotNull(readChunk.Embedding);
        Assert.Equal(768, readChunk.Embedding.Length);
        Assert.Equal(vector[0], readChunk.Embedding[0], precision: 5);
        Assert.Equal(vector[767], readChunk.Embedding[767], precision: 5);
    }

    [Fact]
    public async Task NullEmbedding_IsSupportedBeforeGeneration()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Test Doc Null", "ref-null-1", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Unembedded Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        await using var readContext = _fixture.CreateDbContext();
        var readChunk = await readContext.DocumentChunks
            .FirstOrDefaultAsync(c => c.Id == chunk.Id && c.TenantId == tenant.Id);

        Assert.NotNull(readChunk);
        Assert.Null(readChunk.Embedding);
    }

    [Fact]
    public async Task VectorDimension768_IsAccepted()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Doc 768", "ref-768", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "768 Dim Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var validVector = new float[768];
        Array.Fill(validVector, 0.5f);

        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, validVector) }, 768, CancellationToken.None);

        await using var readContext = _fixture.CreateDbContext();
        var readChunk = await readContext.DocumentChunks.FirstAsync(c => c.Id == chunk.Id);
        Assert.NotNull(readChunk.Embedding);
        Assert.Equal(768, readChunk.Embedding.Length);
    }

    [Fact]
    public async Task WrongDimension_IsRejected()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Doc Bad Dim", "ref-bad-dim", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Bad Dim Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var wrongVector = new float[128]; // Wrong dimension!

        await Assert.ThrowsAnyAsync<Exception>(() =>
            repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, wrongVector) }, 768, CancellationToken.None));
    }

    [Fact]
    public async Task Vector_CanBeUpdated()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Doc Update", "ref-update-1", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Update Vector Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var initialVector = new float[768];
        Array.Fill(initialVector, 0.1f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, initialVector) }, 768, CancellationToken.None);

        var updatedVector = new float[768];
        Array.Fill(updatedVector, 0.9f);
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, updatedVector) }, 768, CancellationToken.None);

        await using var readContext = _fixture.CreateDbContext();
        var readChunk = await readContext.DocumentChunks.FirstAsync(c => c.Id == chunk.Id);
        Assert.NotNull(readChunk.Embedding);
        Assert.Equal(0.9f, readChunk.Embedding[0], precision: 5);
    }

    [Fact]
    public async Task CrossTenantEmbeddingUpdate_IsRejected()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant1 = await CreateTenantAsync(context, "Tenant 1");
        var tenant2 = await CreateTenantAsync(context, "Tenant 2");
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant1.Id, "Tenant1 Doc", "ref-cross-tenant-1", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant1.Id, document.Id, 0, "Tenant1 Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var vector = new float[768];
        Array.Fill(vector, 0.3f);

        // Attempt write using tenant2.Id context
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.PersistEmbeddingsAsync(tenant2.Id, new[] { (chunk.Id, vector) }, 768, CancellationToken.None));
    }

    [Fact]
    public async Task Migrations_InitializeCorrectly()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task HnswVectorIndex_ExistsOnDocumentChunks()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT indexname FROM pg_indexes WHERE indexname = 'IX_DocumentChunks_Embedding'")
            .ToListAsync();

        Assert.Single(indexes);
        Assert.Equal("IX_DocumentChunks_Embedding", indexes[0]);
    }

    [Fact]
    public async Task RepeatedEmbeddingUpdate_IsIdempotent()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repo = new DocumentRepository(context);

        var document = new Document(Guid.NewGuid(), tenant.Id, "Doc Idempotent", "ref-idempotent-1", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, document.Id, 0, "Idempotent Chunk");

        await repo.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var vector = new float[768];
        Array.Fill(vector, 0.42f);

        // Write first time
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, vector) }, 768, CancellationToken.None);

        // Write second time (identical vector and chunk)
        await repo.PersistEmbeddingsAsync(tenant.Id, new[] { (chunk.Id, vector) }, 768, CancellationToken.None);

        await using var readContext = _fixture.CreateDbContext();
        var count = await readContext.DocumentChunks.CountAsync(c => c.DocumentId == document.Id);
        var readChunk = await readContext.DocumentChunks.FirstAsync(c => c.Id == chunk.Id);

        Assert.Equal(1, count); // No duplicate chunk rows created
        Assert.NotNull(readChunk.Embedding);
        Assert.Equal(0.42f, readChunk.Embedding[0], precision: 5);
    }
}
