using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

public sealed class DocumentRepositoryIntegrationTests
{
    private static GovernmentDomainCopilotDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new GovernmentDomainCopilotDbContext(options);
    }

    [Fact]
    public async Task SaveAsync_persists_document_and_chunks_atomically()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new DocumentRepository(context);

        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var document = new Document(documentId, tenantId, "Policy 101", "gov-ref-101", DateTimeOffset.UtcNow);

        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantId, documentId, 0, "Chunk 0 content"),
            new(Guid.NewGuid(), tenantId, documentId, 1, "Chunk 1 content")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        var retrievedDoc = await repository.GetByIdAsync(tenantId, documentId, CancellationToken.None);
        var retrievedChunks = await repository.GetChunksByDocumentIdAsync(tenantId, documentId, CancellationToken.None);

        Assert.NotNull(retrievedDoc);
        Assert.Equal("Policy 101", retrievedDoc.Title);
        Assert.Equal(2, retrievedChunks.Count);
        Assert.Equal("Chunk 0 content", retrievedChunks[0].Content);
        Assert.Equal("Chunk 1 content", retrievedChunks[1].Content);
    }

    [Fact]
    public async Task SaveAsync_preserves_chunk_sequence_ordering()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new DocumentRepository(context);

        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var document = new Document(documentId, tenantId, "Policy 102", "gov-ref-102", DateTimeOffset.UtcNow);

        // Add chunks out of order
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantId, documentId, 2, "Third chunk"),
            new(Guid.NewGuid(), tenantId, documentId, 0, "First chunk"),
            new(Guid.NewGuid(), tenantId, documentId, 1, "Second chunk")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        var retrievedChunks = await repository.GetChunksByDocumentIdAsync(tenantId, documentId, CancellationToken.None);

        Assert.Equal(3, retrievedChunks.Count);
        Assert.Equal(0, retrievedChunks[0].Sequence);
        Assert.Equal("First chunk", retrievedChunks[0].Content);
        Assert.Equal(1, retrievedChunks[1].Sequence);
        Assert.Equal("Second chunk", retrievedChunks[1].Content);
        Assert.Equal(2, retrievedChunks[2].Sequence);
        Assert.Equal("Third chunk", retrievedChunks[2].Content);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_reads()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new DocumentRepository(context);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var document = new Document(documentId, tenantA, "Tenant A Doc", "ref-tenant-a", DateTimeOffset.UtcNow);
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantA, documentId, 0, "Tenant A Chunk")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        // Read using Tenant B ID
        var crossTenantDoc = await repository.GetByIdAsync(tenantB, documentId, CancellationToken.None);
        var crossTenantRefDoc = await repository.GetBySourceReferenceAsync(tenantB, "ref-tenant-a", CancellationToken.None);
        var crossTenantChunks = await repository.GetChunksByDocumentIdAsync(tenantB, documentId, CancellationToken.None);

        Assert.Null(crossTenantDoc);
        Assert.Null(crossTenantRefDoc);
        Assert.Empty(crossTenantChunks);
    }

    [Fact]
    public async Task SaveAsync_rejects_cross_tenant_chunks_and_throws_InvalidOperationException()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new DocumentRepository(context);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid(); // Mismatched tenant
        var documentId = Guid.NewGuid();

        var document = new Document(documentId, tenantA, "Tenant A Doc", "ref-cross-tenant", DateTimeOffset.UtcNow);
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantB, documentId, 0, "Cross-tenant Chunk") // TenantB instead of TenantA
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(document, chunks, CancellationToken.None));

        Assert.Contains("Cross-tenant chunk persistence attempt detected", exception.Message);
    }

    [Fact]
    public async Task SaveAsync_idempotently_updates_document_and_replaces_chunks_on_repeated_ingestion()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new DocumentRepository(context);

        var tenantId = Guid.NewGuid();
        var sourceRef = "stable-gov-regulation-42";

        // Initial ingestion
        var doc1 = new Document(Guid.NewGuid(), tenantId, "Draft Regulation V1", sourceRef, DateTimeOffset.UtcNow);
        var chunks1 = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantId, doc1.Id, 0, "Version 1 Chunk 0"),
            new(Guid.NewGuid(), tenantId, doc1.Id, 1, "Version 1 Chunk 1")
        };
        await repository.SaveAsync(doc1, chunks1, CancellationToken.None);

        // Re-ingestion with same source reference but updated content
        var doc2 = new Document(Guid.NewGuid(), tenantId, "Final Regulation V2", sourceRef, DateTimeOffset.UtcNow);
        doc2.MarkAsCompleted();

        var chunks2 = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantId, doc2.Id, 0, "Version 2 Chunk 0"),
            new(Guid.NewGuid(), tenantId, doc2.Id, 1, "Version 2 Chunk 1"),
            new(Guid.NewGuid(), tenantId, doc2.Id, 2, "Version 2 Chunk 2")
        };
        await repository.SaveAsync(doc2, chunks2, CancellationToken.None);

        var updatedDoc = await repository.GetBySourceReferenceAsync(tenantId, sourceRef, CancellationToken.None);
        Assert.NotNull(updatedDoc);
        Assert.Equal("Final Regulation V2", updatedDoc.Title);
        Assert.Equal(DocumentIngestionStatus.Completed, updatedDoc.IngestionStatus);

        var updatedChunks = await repository.GetChunksByDocumentIdAsync(tenantId, updatedDoc.Id, CancellationToken.None);
        Assert.Equal(3, updatedChunks.Count);
        Assert.Equal("Version 2 Chunk 0", updatedChunks[0].Content);
        Assert.Equal("Version 2 Chunk 1", updatedChunks[1].Content);
        Assert.Equal("Version 2 Chunk 2", updatedChunks[2].Content);
    }
}
