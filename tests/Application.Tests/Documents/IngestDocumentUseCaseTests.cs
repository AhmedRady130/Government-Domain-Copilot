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

    // --- Stubs / Fakes ---

    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid GetTenantId() => tenantId;
    }

    private sealed class FakeDocumentChunker(IReadOnlyList<ChunkData> chunksToReturn) : IDocumentChunker
    {
        public IReadOnlyList<ChunkData> Chunk(string normalizedText) => chunksToReturn;
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
