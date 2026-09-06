using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Models;
using GovernmentDomainCopilot.Application.Documents.Validation;
using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Documents;

/// <summary>
/// Orchestrates the document ingestion use case with robust failure handling and status reporting.
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

        // 1. Validate request payload constraints (occurs before persistence)
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

        // 3. Instantiate Document entity starting in Pending status
        var documentId = Guid.NewGuid();
        var document = new Document(
            documentId,
            tenantId,
            command.Title.Trim(),
            command.SourceReference.Trim(),
            DateTimeOffset.UtcNow,
            DocumentIngestionStatus.Pending);

        // 4. Perform chunking & normalization with controlled failure handling
        IReadOnlyList<ChunkData> chunkDataList;
        try
        {
            chunkDataList = _chunker.Chunk(command.SourceText);
            if (chunkDataList == null || chunkDataList.Count == 0)
            {
                var failureReason = "Document chunker produced no valid content chunks.";
                document.MarkAsFailed(failureReason);
                await SaveFailedDocumentAsync(document, cancellationToken);
                return new IngestDocumentResult(document.Id, 0, DocumentIngestionStatus.Failed, failureReason);
            }
        }
        catch (Exception ex)
        {
            var failureReason = $"Chunking processing failed: {ex.Message}";
            document.MarkAsFailed(failureReason);
            await SaveFailedDocumentAsync(document, cancellationToken);
            return new IngestDocumentResult(document.Id, 0, DocumentIngestionStatus.Failed, document.FailureReason);
        }

        // 5. Build chunks for completed document
        var chunks = chunkDataList
            .Select(c => new DocumentChunk(Guid.NewGuid(), tenantId, documentId, c.Sequence, c.Content))
            .ToList();

        // 6. Transition document to Completed status and attempt atomic save
        document.MarkAsCompleted();

        try
        {
            await _repository.SaveAsync(document, chunks, cancellationToken);
        }
        catch (Exception ex)
        {
            // If completed persistence fails, attempt safe transition to Failed so document doesn't remain Pending
            try
            {
                var failedDoc = new Document(
                    documentId,
                    tenantId,
                    command.Title.Trim(),
                    command.SourceReference.Trim(),
                    document.CreatedAtUtc,
                    DocumentIngestionStatus.Pending);

                failedDoc.MarkAsFailed($"Persistence failed: {ex.Message}");
                await SaveFailedDocumentAsync(failedDoc, cancellationToken);
            }
            catch
            {
                // Ignore secondary save errors so primary exception propagates cleanly
            }

            throw;
        }

        // 7. Return typed use-case result
        return new IngestDocumentResult(document.Id, chunks.Count, DocumentIngestionStatus.Completed);
    }

    private async Task SaveFailedDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveAsync(document, Array.Empty<DocumentChunk>(), cancellationToken);
        }
        catch
        {
            // Best effort persistence of failure state
        }
    }
}
