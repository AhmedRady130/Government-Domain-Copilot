using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Abstractions;
using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernmentDomainCopilot.Infrastructure.Documents;

/// <summary>
/// Infrastructure EF Core implementation of <see cref="IDocumentRepository"/> and <see cref="IChunkEmbeddingRepository"/>.
/// </summary>
public sealed class DocumentRepository : IDocumentRepository, IChunkEmbeddingRepository
{
    private readonly GovernmentDomainCopilotDbContext _context;

    public DocumentRepository(GovernmentDomainCopilotDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task SaveAsync(
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var safeChunks = chunks ?? Array.Empty<DocumentChunk>();

        if (document.IngestionStatus != DocumentIngestionStatus.Failed && safeChunks.Count == 0)
        {
            throw new ArgumentException("Chunks list cannot be null or empty for non-failed documents.", nameof(chunks));
        }

        if (safeChunks.Count > 0)
        {
            // Multi-tenancy guard: verify all chunks belong to the document's TenantId
            if (safeChunks.Any(c => c.TenantId != document.TenantId))
            {
                throw new InvalidOperationException(
                    "Cross-tenant chunk persistence attempt detected. All chunks must match the document TenantId.");
            }

            // Parent relationship guard: verify all chunks reference the document's Id
            if (safeChunks.Any(c => c.DocumentId != document.Id))
            {
                throw new InvalidOperationException(
                    "All chunks must reference the document ID being persisted.");
            }
        }

        var executionStrategy = _context.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Idempotency check: look for existing document with same TenantId and SourceReference
            var existingDocument = await _context.Documents
                .FirstOrDefaultAsync(
                    d => d.TenantId == document.TenantId && d.SourceReference == document.SourceReference,
                    cancellationToken);

            if (existingDocument != null)
            {
                // Delete previous chunks belonging to the existing document
                var oldChunks = await _context.DocumentChunks
                    .Where(c => c.TenantId == existingDocument.TenantId && c.DocumentId == existingDocument.Id)
                    .ToListAsync(cancellationToken);

                _context.DocumentChunks.RemoveRange(oldChunks);

                if (existingDocument.Id == document.Id)
                {
                    _context.Entry(existingDocument).CurrentValues.SetValues(document);
                    if (safeChunks.Count > 0)
                    {
                        _context.DocumentChunks.AddRange(safeChunks);
                    }
                }
                else
                {
                    _context.Documents.Remove(existingDocument);
                    _context.Documents.Add(document);
                    if (safeChunks.Count > 0)
                    {
                        _context.DocumentChunks.AddRange(safeChunks);
                    }
                }
            }
            else
            {
                _context.Documents.Add(document);
                if (safeChunks.Count > 0)
                {
                    _context.DocumentChunks.AddRange(safeChunks);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<Document?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "TenantId cannot be empty.");
        }

        return await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
    }

    public async Task<Document?> GetBySourceReferenceAsync(
        Guid tenantId,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "TenantId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return null;
        }

        return await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId && d.SourceReference == sourceReference,
                cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksByDocumentIdAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "TenantId cannot be empty.");
        }

        return await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DocumentId == documentId)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);
    }

    public async Task PersistEmbeddingsAsync(
        Guid tenantId,
        IReadOnlyList<(Guid ChunkId, float[] Vector)> embeddings,
        int expectedDimension,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "TenantId cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(embeddings);

        if (embeddings.Count == 0)
        {
            return;
        }

        var chunkIds = embeddings.Select(e => e.ChunkId).Distinct().ToList();

        var chunks = await _context.DocumentChunks
            .Where(c => chunkIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (chunks.Count != chunkIds.Count)
        {
            throw new InvalidOperationException("One or more chunk IDs were not found in the database.");
        }

        if (chunks.Any(c => c.TenantId != tenantId))
        {
            throw new InvalidOperationException("Cross-tenant embedding write detected. All chunks must belong to tenantId.");
        }

        var embeddingMap = embeddings.ToDictionary(e => e.ChunkId, e => e.Vector);

        foreach (var chunk in chunks)
        {
            if (embeddingMap.TryGetValue(chunk.Id, out var vector))
            {
                chunk.SetEmbedding(vector, expectedDimension);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
