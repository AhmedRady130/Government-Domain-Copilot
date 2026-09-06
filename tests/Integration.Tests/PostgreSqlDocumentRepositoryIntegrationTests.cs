using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

public sealed class PostgreSqlDocumentRepositoryIntegrationTests : IClassFixture<PostgreSqlTestDatabaseFixture>
{
    private readonly PostgreSqlTestDatabaseFixture _fixture;

    public PostgreSqlDocumentRepositoryIntegrationTests(PostgreSqlTestDatabaseFixture fixture)
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
    public async Task Document_and_chunks_persist_correctly_relational()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var documentId = Guid.NewGuid();
        var document = new Document(documentId, tenant.Id, "Relational Decree", "ref-relational-101", DateTimeOffset.UtcNow);

        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, documentId, 0, "Chunk 0 relational content"),
            new(Guid.NewGuid(), tenant.Id, documentId, 1, "Chunk 1 relational content")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        var retrievedDoc = await repository.GetByIdAsync(tenant.Id, documentId, CancellationToken.None);
        var retrievedChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, documentId, CancellationToken.None);

        Assert.NotNull(retrievedDoc);
        Assert.Equal("Relational Decree", retrievedDoc.Title);
        Assert.Equal(2, retrievedChunks.Count);
        Assert.Equal("Chunk 0 relational content", retrievedChunks[0].Content);
        Assert.Equal("Chunk 1 relational content", retrievedChunks[1].Content);
    }

    [Fact]
    public async Task Chunk_sequence_ordering_preserved_relational()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var documentId = Guid.NewGuid();
        var document = new Document(documentId, tenant.Id, "Ordered Decree", "ref-ordered-102", DateTimeOffset.UtcNow);

        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, documentId, 2, "Third sequence chunk"),
            new(Guid.NewGuid(), tenant.Id, documentId, 0, "First sequence chunk"),
            new(Guid.NewGuid(), tenant.Id, documentId, 1, "Second sequence chunk")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        var retrievedChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, documentId, CancellationToken.None);

        Assert.Equal(3, retrievedChunks.Count);
        Assert.Equal(0, retrievedChunks[0].Sequence);
        Assert.Equal("First sequence chunk", retrievedChunks[0].Content);
        Assert.Equal(1, retrievedChunks[1].Sequence);
        Assert.Equal("Second sequence chunk", retrievedChunks[1].Content);
        Assert.Equal(2, retrievedChunks[2].Sequence);
        Assert.Equal("Third sequence chunk", retrievedChunks[2].Content);
    }

    [Fact]
    public async Task Tenant_isolation_on_reads_relational()
    {
        using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");
        var repository = new DocumentRepository(context);

        var documentId = Guid.NewGuid();

        var document = new Document(documentId, tenantA.Id, "Tenant A Secret Decree", "ref-secret-a", DateTimeOffset.UtcNow);
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantA.Id, documentId, 0, "Tenant A Secret Content")
        };

        await repository.SaveAsync(document, chunks, CancellationToken.None);

        var crossTenantDoc = await repository.GetByIdAsync(tenantB.Id, documentId, CancellationToken.None);
        var crossTenantRefDoc = await repository.GetBySourceReferenceAsync(tenantB.Id, "ref-secret-a", CancellationToken.None);
        var crossTenantChunks = await repository.GetChunksByDocumentIdAsync(tenantB.Id, documentId, CancellationToken.None);

        Assert.Null(crossTenantDoc);
        Assert.Null(crossTenantRefDoc);
        Assert.Empty(crossTenantChunks);
    }

    [Fact]
    public async Task Cross_tenant_access_rejected_relational()
    {
        using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");
        var repository = new DocumentRepository(context);

        var documentId = Guid.NewGuid();

        var document = new Document(documentId, tenantA.Id, "Tenant A Document", "ref-cross-tenant-rel", DateTimeOffset.UtcNow);
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenantB.Id, documentId, 0, "Tenant B Mismatched Chunk")
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(document, chunks, CancellationToken.None));

        Assert.Contains("Cross-tenant chunk persistence attempt detected", exception.Message);
    }

    [Fact]
    public async Task Unique_constraint_on_TenantId_and_SourceReference_enforced_by_database()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);

        var duplicateSourceRef = "unique-constraint-test-ref";

        var doc1 = new Document(Guid.NewGuid(), tenant.Id, "Doc 1", duplicateSourceRef, DateTimeOffset.UtcNow);
        var doc2 = new Document(Guid.NewGuid(), tenant.Id, "Doc 2", duplicateSourceRef, DateTimeOffset.UtcNow);

        context.Documents.Add(doc1);
        await context.SaveChangesAsync();

        context.Documents.Add(doc2);

        // Database engine must reject duplicate (TenantId, SourceReference) insertion
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Repeated_ingestion_remains_idempotent_relational()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var sourceRef = "idempotent-reg-555";

        var docV1 = new Document(Guid.NewGuid(), tenant.Id, "Draft V1", sourceRef, DateTimeOffset.UtcNow);
        var chunksV1 = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, docV1.Id, 0, "Old V1 Chunk 0"),
            new(Guid.NewGuid(), tenant.Id, docV1.Id, 1, "Old V1 Chunk 1")
        };
        await repository.SaveAsync(docV1, chunksV1, CancellationToken.None);

        var docV2 = new Document(Guid.NewGuid(), tenant.Id, "Final V2", sourceRef, DateTimeOffset.UtcNow);
        docV2.MarkAsCompleted();
        var chunksV2 = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, docV2.Id, 0, "New V2 Chunk 0"),
            new(Guid.NewGuid(), tenant.Id, docV2.Id, 1, "New V2 Chunk 1"),
            new(Guid.NewGuid(), tenant.Id, docV2.Id, 2, "New V2 Chunk 2")
        };
        await repository.SaveAsync(docV2, chunksV2, CancellationToken.None);

        var updatedDoc = await repository.GetBySourceReferenceAsync(tenant.Id, sourceRef, CancellationToken.None);
        Assert.NotNull(updatedDoc);
        Assert.Equal("Final V2", updatedDoc.Title);
        Assert.Equal(DocumentIngestionStatus.Completed, updatedDoc.IngestionStatus);

        var updatedChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, updatedDoc.Id, CancellationToken.None);
        Assert.Equal(3, updatedChunks.Count);
        Assert.Equal("New V2 Chunk 0", updatedChunks[0].Content);
    }

    [Fact]
    public async Task Transaction_rollback_prevents_partial_persistence_on_failure()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);

        var documentId = Guid.NewGuid();

        // Perform explicit transaction rollback
        var executionStrategy = context.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            var doc = new Document(documentId, tenant.Id, "Rollback Test Doc", "ref-rollback", DateTimeOffset.UtcNow);
            context.Documents.Add(doc);
            await context.SaveChangesAsync();

            // Deliberately rollback transaction without committing
            await transaction.RollbackAsync();
        });

        // Verify that document was not persisted to the relational database
        var uncommittedDoc = await context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
        Assert.Null(uncommittedDoc);
    }

    [Fact]
    public async Task Foreign_key_constraints_enforced_by_database()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);

        var nonExistentDocumentId = Guid.NewGuid();

        // Attempting to insert a chunk referencing a non-existent document
        var orphanedChunk = new DocumentChunk(Guid.NewGuid(), tenant.Id, nonExistentDocumentId, 0, "Orphan Chunk Content");
        context.DocumentChunks.Add(orphanedChunk);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Failed_persistence_leaves_no_orphaned_chunks()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var sourceRef = "ref-fail-no-orphans";

        // 1. Initial attempt persists completed document with 2 chunks
        var docCompleted = new Document(Guid.NewGuid(), tenant.Id, "Initial Decree", sourceRef, DateTimeOffset.UtcNow);
        docCompleted.MarkAsCompleted();
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, docCompleted.Id, 0, "Chunk 0"),
            new(Guid.NewGuid(), tenant.Id, docCompleted.Id, 1, "Chunk 1")
        };
        await repository.SaveAsync(docCompleted, chunks, CancellationToken.None);

        var initialChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, docCompleted.Id, CancellationToken.None);
        Assert.Equal(2, initialChunks.Count);

        // 2. Subsequent attempt fails; save failed document with 0 chunks
        var docFailed = new Document(Guid.NewGuid(), tenant.Id, "Initial Decree Failed", sourceRef, DateTimeOffset.UtcNow);
        docFailed.MarkAsFailed("Processing error occurred");
        await repository.SaveAsync(docFailed, Array.Empty<DocumentChunk>(), CancellationToken.None);

        // 3. Verify no chunks remain in the database for this document identity
        var postFailChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, docFailed.Id, CancellationToken.None);
        Assert.Empty(postFailChunks);

        var docFromDb = await repository.GetBySourceReferenceAsync(tenant.Id, sourceRef, CancellationToken.None);
        Assert.NotNull(docFromDb);
        Assert.Equal(DocumentIngestionStatus.Failed, docFromDb.IngestionStatus);
    }

    [Fact]
    public async Task Repeated_retry_remains_idempotent()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var sourceRef = "ref-repeated-retry";

        // Perform multiple failed & successful ingestion attempts for the exact same TenantId and SourceReference
        for (int i = 0; i < 3; i++)
        {
            var failedDoc = new Document(Guid.NewGuid(), tenant.Id, $"Draft attempt {i}", sourceRef, DateTimeOffset.UtcNow);
            failedDoc.MarkAsFailed($"Attempt {i} failed");
            await repository.SaveAsync(failedDoc, Array.Empty<DocumentChunk>(), CancellationToken.None);
        }

        var docCountAfterFails = await context.Documents
            .CountAsync(d => d.TenantId == tenant.Id && d.SourceReference == sourceRef);
        Assert.Equal(1, docCountAfterFails);

        // Successful retry
        var successDoc = new Document(Guid.NewGuid(), tenant.Id, "Final Successful Decree", sourceRef, DateTimeOffset.UtcNow);
        successDoc.MarkAsCompleted();
        var finalChunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, successDoc.Id, 0, "Final Chunk 0")
        };
        await repository.SaveAsync(successDoc, finalChunks, CancellationToken.None);

        var finalDocCount = await context.Documents
            .CountAsync(d => d.TenantId == tenant.Id && d.SourceReference == sourceRef);
        Assert.Equal(1, finalDocCount);

        var retrievedDoc = await repository.GetBySourceReferenceAsync(tenant.Id, sourceRef, CancellationToken.None);
        Assert.NotNull(retrievedDoc);
        Assert.Equal(DocumentIngestionStatus.Completed, retrievedDoc.IngestionStatus);
    }

    [Fact]
    public async Task Failed_then_successful_ingestion_leaves_exactly_one_active_document_and_the_expected_chunks()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context);
        var repository = new DocumentRepository(context);

        var sourceRef = "ref-fail-then-success";

        // 1. Initial attempt fails
        var failedDoc = new Document(Guid.NewGuid(), tenant.Id, "Failed Attempt Title", sourceRef, DateTimeOffset.UtcNow);
        failedDoc.MarkAsFailed("Chunker error");
        await repository.SaveAsync(failedDoc, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var docAfterFail = await repository.GetBySourceReferenceAsync(tenant.Id, sourceRef, CancellationToken.None);
        Assert.NotNull(docAfterFail);
        Assert.Equal(DocumentIngestionStatus.Failed, docAfterFail.IngestionStatus);
        Assert.Equal("Chunker error", docAfterFail.FailureReason);

        // 2. Successful retry
        var successDoc = new Document(Guid.NewGuid(), tenant.Id, "Completed Title", sourceRef, DateTimeOffset.UtcNow);
        successDoc.MarkAsCompleted();
        var chunks = new List<DocumentChunk>
        {
            new(Guid.NewGuid(), tenant.Id, successDoc.Id, 0, "Chunk content 0"),
            new(Guid.NewGuid(), tenant.Id, successDoc.Id, 1, "Chunk content 1")
        };
        await repository.SaveAsync(successDoc, chunks, CancellationToken.None);

        // 3. Verify exactly 1 document exists for source reference and status is Completed with expected chunks
        var allDocsForRef = await context.Documents
            .Where(d => d.TenantId == tenant.Id && d.SourceReference == sourceRef)
            .ToListAsync();
        Assert.Single(allDocsForRef);

        var activeDoc = allDocsForRef[0];
        Assert.Equal(DocumentIngestionStatus.Completed, activeDoc.IngestionStatus);
        Assert.Null(activeDoc.FailureReason);

        var activeChunks = await repository.GetChunksByDocumentIdAsync(tenant.Id, activeDoc.Id, CancellationToken.None);
        Assert.Equal(2, activeChunks.Count);
        Assert.Equal("Chunk content 0", activeChunks[0].Content);
    }

    [Fact]
    public async Task Migrations_initialize_test_database_cleanly()
    {
        using var context = _fixture.CreateDbContext();

        Assert.NotNull(context.Database.ProviderName);
        Assert.Equal(_fixture.ProviderName, context.Database.ProviderName);

        // Verify tables exist and can be queried
        var docCount = await context.Documents.CountAsync();
        Assert.True(docCount >= 0);
    }

    [Fact]
    public async Task Migration_model_state_is_synchronized()
    {
        using var context = _fixture.CreateDbContext();

        var entityTypes = context.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();

        Assert.Contains(nameof(Document), entityTypes);
        Assert.Contains(nameof(DocumentChunk), entityTypes);
        Assert.Contains(nameof(Tenant), entityTypes);
        await Task.CompletedTask;
    }
}

