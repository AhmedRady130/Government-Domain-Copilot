using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Models;
using GovernmentDomainCopilot.Application.Documents.Validation;
using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Documents;

/// <summary>
/// Orchestrates the document ingestion use case.
/// </summary>
public sealed class IngestDocumentUseCase : IIngestDocumentUseCase
{
    private readonly ITenantContext _tenantContext;
    private readonly IDocumentChunker _chunker;
    private readonly IDocumentRepository _repository;

    public IngestDocumentUseCase(
        ITenantContext tenantContext,
        IDocumentChunker chunker,
        IDocumentRepository repository)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IngestDocumentResult> IngestAsync(
        IngestDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Validate request payload constraints
        var validationErrors = IngestDocumentCommandValidator.Validate(command);
        if (validationErrors.Count > 0)
        {
            throw new IngestionValidationException(validationErrors);
        }

        // 2. Resolve server-side tenant identity
        var tenantId = _tenantContext.GetTenantId();
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Authenticated tenant context is missing or invalid.");
        }

        // 3. Chunk source text
        var chunkDataList = _chunker.Chunk(command.SourceText);
        if (chunkDataList.Count == 0)
        {
            throw new InvalidOperationException("Document chunker produced no valid content chunks.");
        }

        // 4. Create Domain entities owned by current tenant
        var documentId = Guid.NewGuid();
        var document = new Document(
            documentId,
            tenantId,
            command.Title.Trim(),
            command.SourceReference.Trim(),
            DateTimeOffset.UtcNow,
            DocumentIngestionStatus.Pending);

        var chunks = chunkDataList
            .Select(c => new DocumentChunk(Guid.NewGuid(), tenantId, documentId, c.Sequence, c.Content))
            .ToList();

        // 5. Transition document status to Completed and persist document + chunks atomically
        document.MarkAsCompleted();
        await _repository.SaveAsync(document, chunks, cancellationToken);

        // 6. Return typed use-case result
        return new IngestDocumentResult(document.Id, chunks.Count);
    }
}
