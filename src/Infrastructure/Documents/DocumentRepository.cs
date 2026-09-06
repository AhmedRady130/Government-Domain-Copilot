using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernmentDomainCopilot.Infrastructure.Documents;

/// <summary>
/// Infrastructure EF Core implementation of <see cref="IDocumentRepository"/>.
/// </summary>
public sealed class DocumentRepository : IDocumentRepository
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

        if (chunks == null || chunks.Count == 0)
        {
            throw new ArgumentException("Chunks list cannot be null or empty.", nameof(chunks));
        }

        // Multi-tenancy guard: verify all chunks belong to the document's TenantId
        if (chunks.Any(c => c.TenantId != document.TenantId))
        {
            throw new InvalidOperationException(
                "Cross-tenant chunk persistence attempt detected. All chunks must match the document TenantId.");
        }

        // Parent relationship guard: verify all chunks reference the document's Id
        if (chunks.Any(c => c.DocumentId != document.Id))
        {
            throw new InvalidOperationException(
                "All chunks must reference the document ID being persisted.");
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
                    _context.DocumentChunks.AddRange(chunks);
                }
                else
                {
                    _context.Documents.Remove(existingDocument);
                    _context.Documents.Add(document);
                    _context.DocumentChunks.AddRange(chunks);
                }
            }
            else
            {
                _context.Documents.Add(document);
                _context.DocumentChunks.AddRange(chunks);
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
}
