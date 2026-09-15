using GovernmentDomainCopilot.Application.Corpus;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Models;
using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Tests.Corpus;

public sealed class CorpusValidatorTests
{
    [Fact]
    public void Committed_corpus_meets_the_assessment_thresholds_and_has_complete_metadata()
    {
        var corpusDirectory = Path.Combine(FindRepositoryRoot(), "data", "corpus");
        var manifest = CorpusManifest.Parse(File.ReadAllText(Path.Combine(corpusDirectory, "manifest.json")));

        var result = CorpusValidator.Validate(manifest, relativePath => File.ReadAllText(Path.Combine(corpusDirectory, relativePath)));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.True(result.DocumentCount >= 30);
        Assert.True(result.PageCount >= 150);
        Assert.All(manifest.Documents, document => Assert.True(document.IsSynthetic));
    }

    [Fact]
    public void Committed_corpus_has_stable_two_tenant_distribution()
    {
        var corpusDirectory = Path.Combine(FindRepositoryRoot(), "data", "corpus");
        var manifest = CorpusManifest.Parse(File.ReadAllText(Path.Combine(corpusDirectory, "manifest.json")));

        var distribution = manifest.Documents.GroupBy(document => document.TenantId).ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(16, distribution["11111111-1111-1111-1111-111111111111"]);
        Assert.Equal(16, distribution["22222222-2222-2222-2222-222222222222"]);
    }

    [Fact]
    public async Task Representative_manifest_documents_use_the_existing_pipeline_and_repeated_ingestion_is_idempotent()
    {
        var corpusDirectory = Path.Combine(FindRepositoryRoot(), "data", "corpus");
        var manifest = CorpusManifest.Parse(File.ReadAllText(Path.Combine(corpusDirectory, "manifest.json")));
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var repository = new InMemoryDocumentRepository();
        var useCase = new IngestDocumentUseCase(new FixedTenantContext(tenantA), new SingleChunker(), repository);
        var assignedDocuments = manifest.Documents.Where(d => d.TenantId == tenantA.ToString()).Take(2).ToList();

        foreach (var document in assignedDocuments)
            await useCase.IngestAsync(new IngestDocumentCommand(document.Title, document.SourceReference, File.ReadAllText(Path.Combine(corpusDirectory, document.File))), CancellationToken.None);
        foreach (var document in assignedDocuments)
            await useCase.IngestAsync(new IngestDocumentCommand(document.Title, document.SourceReference, File.ReadAllText(Path.Combine(corpusDirectory, document.File))), CancellationToken.None);

        Assert.Equal(2, repository.Documents.Count);
        Assert.All(repository.Documents.Values, document => Assert.Equal(tenantA, document.TenantId));
        Assert.All(assignedDocuments, document => Assert.Equal(document.Title, repository.Documents[(tenantA, document.SourceReference)].Title));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "GovernmentDomainCopilot.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext { public Guid GetTenantId() => tenantId; }
    private sealed class SingleChunker : IDocumentChunker
    {
        public IReadOnlyList<ChunkData> Chunk(string text) => [new ChunkData(0, text)];
    }
    private sealed class InMemoryDocumentRepository : IDocumentRepository
    {
        public Dictionary<(Guid, string), Document> Documents { get; } = [];
        public Task SaveAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken) { Documents[(document.TenantId, document.SourceReference)] = document; return Task.CompletedTask; }
        public Task<Document?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => Task.FromResult(Documents.Values.SingleOrDefault(d => d.TenantId == tenantId && d.Id == id));
        public Task<Document?> GetBySourceReferenceAsync(Guid tenantId, string sourceReference, CancellationToken cancellationToken) => Task.FromResult(Documents.GetValueOrDefault((tenantId, sourceReference)));
        public Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentIdAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DocumentChunk>>([]);
    }
}
