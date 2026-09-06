using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Models;
using GovernmentDomainCopilot.Application.Documents.Validation;
using GovernmentDomainCopilot.Domain.Entities;

namespace Application.Tests.Documents;

public sealed class IngestDocumentUseCaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task IngestAsync_valid_command_persists_document_and_chunks_owned_by_tenant()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeDocumentChunker(new List<ChunkData>
        {
            new(0, "First chunk text"),
            new(1, "Second chunk text")
        });
        var repository = new FakeDocumentRepository();

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Gov Policy 1", "ref-100", "First chunk text Second chunk text");

        var result = await useCase.IngestAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.DocumentId);
        Assert.Equal(2, result.ChunkCount);

        Assert.NotNull(repository.SavedDocument);
        Assert.Equal(_tenantId, repository.SavedDocument.TenantId);
        Assert.Equal("Gov Policy 1", repository.SavedDocument.Title);
        Assert.Equal("ref-100", repository.SavedDocument.SourceReference);
        Assert.Equal(DocumentIngestionStatus.Completed, repository.SavedDocument.IngestionStatus);

        Assert.NotNull(repository.SavedChunks);
        Assert.Equal(2, repository.SavedChunks.Count);
        Assert.All(repository.SavedChunks, chunk => Assert.Equal(_tenantId, chunk.TenantId));
        Assert.All(repository.SavedChunks, chunk => Assert.Equal(repository.SavedDocument.Id, chunk.DocumentId));
    }

    [Fact]
    public async Task IngestAsync_validation_failure_prevents_repository_call_and_throws_IngestionValidationException()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "chunk") });
        var repository = new FakeDocumentRepository();

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var invalidCommand = new IngestDocumentCommand(string.Empty, "ref-100", "source text");

        await Assert.ThrowsAsync<IngestionValidationException>(() => useCase.IngestAsync(invalidCommand, CancellationToken.None));

        Assert.Null(repository.SavedDocument);
        Assert.Null(repository.SavedChunks);
    }

    [Fact]
    public async Task IngestAsync_throws_when_tenant_context_returns_empty_guid()
    {
        var tenantContext = new FakeTenantContext(Guid.Empty);
        var chunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "chunk") });
        var repository = new FakeDocumentRepository();

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Title", "ref-100", "source text");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.IngestAsync(command, CancellationToken.None));

        Assert.Contains("tenant context is missing or invalid", exception.Message);
        Assert.Null(repository.SavedDocument);
    }

    [Fact]
    public async Task IngestAsync_surfaces_repository_exception_when_persistence_fails()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "chunk") });
        var repository = new FakeDocumentRepository { ThrowOnSave = new InvalidOperationException("DB error") };

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Title", "ref-100", "source text");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.IngestAsync(command, CancellationToken.None));

        Assert.Equal("DB error", exception.Message);
    }

    [Fact]
    public async Task Chunker_failure_results_in_safe_failure_behavior()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeFailingDocumentChunker("Invalid encoding in text.");
        var repository = new FakeDocumentRepository();

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Decree Title", "ref-chunker-fail", "Corrupted text");

        var result = await useCase.IngestAsync(command, CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Failed, result.Status);
        Assert.Equal(0, result.ChunkCount);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Chunking processing failed", result.FailureReason);

        Assert.NotNull(repository.SavedDocument);
        Assert.Equal(DocumentIngestionStatus.Failed, repository.SavedDocument.IngestionStatus);
        Assert.Empty(repository.SavedChunks!);
    }

    [Fact]
    public async Task Repository_failure_results_in_safe_failure_behavior()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "Valid content") });
        var repository = new FakeDocumentRepository { ThrowOnSave = new InvalidOperationException("Database constraint violation") };

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Decree Title", "ref-repo-fail", "Valid source text");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.IngestAsync(command, CancellationToken.None));
        Assert.Equal("Database constraint violation", exception.Message);
    }

    [Fact]
    public async Task Document_does_not_incorrectly_remain_Pending_after_controlled_failure()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var chunker = new FakeDocumentChunker(Array.Empty<ChunkData>());
        var repository = new FakeDocumentRepository();

        var useCase = new IngestDocumentUseCase(tenantContext, chunker, repository);
        var command = new IngestDocumentCommand("Title", "ref-no-chunks", "No chunks text");

        var result = await useCase.IngestAsync(command, CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Failed, result.Status);
        Assert.NotNull(repository.SavedDocument);
        Assert.NotEqual(DocumentIngestionStatus.Pending, repository.SavedDocument.IngestionStatus);
        Assert.Equal(DocumentIngestionStatus.Failed, repository.SavedDocument.IngestionStatus);
    }

    [Fact]
    public async Task Successful_retry_completes_the_document()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var repository = new FakeDocumentRepository();

        // 1. Initial attempt fails due to chunker failure
        var failingChunker = new FakeDocumentChunker(Array.Empty<ChunkData>());
        var failingUseCase = new IngestDocumentUseCase(tenantContext, failingChunker, repository);
        var command = new IngestDocumentCommand("Retry Decree", "ref-retry-101", "Text");

        var firstResult = await failingUseCase.IngestAsync(command, CancellationToken.None);
        Assert.Equal(DocumentIngestionStatus.Failed, firstResult.Status);
        Assert.Equal(DocumentIngestionStatus.Failed, repository.SavedDocument!.IngestionStatus);

        // 2. Retry with valid chunker succeeds
        var validChunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "Valid chunk") });
        var retryUseCase = new IngestDocumentUseCase(tenantContext, validChunker, repository);

        var retryResult = await retryUseCase.IngestAsync(command, CancellationToken.None);

        Assert.Equal(DocumentIngestionStatus.Completed, retryResult.Status);
        Assert.Equal(1, retryResult.ChunkCount);
        Assert.Equal(DocumentIngestionStatus.Completed, repository.SavedDocument!.IngestionStatus);
        Assert.Null(repository.SavedDocument.FailureReason);
        Assert.Single(repository.SavedChunks!);
    }

    [Fact]
    public async Task Tenant_context_is_still_enforced_during_retry()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var repository = new FakeDocumentRepository();
        var chunker = new FakeDocumentChunker(new List<ChunkData> { new(0, "Chunk text") });

        var useCaseA = new IngestDocumentUseCase(new FakeTenantContext(tenantA), chunker, repository);
        var command = new IngestDocumentCommand("Tenant Doc", "ref-tenant-retry", "Text");

        var resultA = await useCaseA.IngestAsync(command, CancellationToken.None);
        Assert.Equal(tenantA, repository.SavedDocument!.TenantId);

        var useCaseB = new IngestDocumentUseCase(new FakeTenantContext(tenantB), chunker, repository);
        var resultB = await useCaseB.IngestAsync(command, CancellationToken.None);
        Assert.Equal(tenantB, repository.SavedDocument!.TenantId);
    }

    // --- Stubs / Fakes ---

    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid GetTenantId() => tenantId;
    }

    private sealed class FakeDocumentChunker(IReadOnlyList<ChunkData> chunksToReturn) : IDocumentChunker
    {
        public IReadOnlyList<ChunkData> Chunk(string normalizedText) => chunksToReturn;
    }

    private sealed class FakeFailingDocumentChunker(string errorMessage) : IDocumentChunker
    {
        public IReadOnlyList<ChunkData> Chunk(string normalizedText) => throw new InvalidOperationException(errorMessage);
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Document? SavedDocument { get; private set; }
        public IReadOnlyList<DocumentChunk>? SavedChunks { get; private set; }
        public Exception? ThrowOnSave { get; init; }

        public Task SaveAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
        {
            if (ThrowOnSave != null)
            {
                throw ThrowOnSave;
            }

            SavedDocument = document;
            SavedChunks = chunks;
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(SavedDocument?.TenantId == tenantId && SavedDocument?.Id == id ? SavedDocument : null);
        }

        public Task<Document?> GetBySourceReferenceAsync(Guid tenantId, string sourceReference, CancellationToken cancellationToken)
        {
            return Task.FromResult(SavedDocument?.TenantId == tenantId && SavedDocument?.SourceReference == sourceReference ? SavedDocument : null);
        }

        public Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentIdAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(
                SavedDocument?.TenantId == tenantId && SavedDocument?.Id == documentId && SavedChunks != null
                    ? SavedChunks
                    : Array.Empty<DocumentChunk>());
        }
    }
}
