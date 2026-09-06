namespace GovernmentDomainCopilot.Infrastructure.Retrieval;

using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

public sealed class PgVectorChunkRetriever : IChunkRetriever
{
    private readonly GovernmentDomainCopilotDbContext _dbContext;

    public PgVectorChunkRetriever(GovernmentDomainCopilotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
        Guid tenantId,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        if (queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be empty.", nameof(queryVector));
        }

        if (topK <= 0)
        {
            throw new ArgumentException("TopK must be positive.", nameof(topK));
        }

        var targetVector = new Vector(queryVector);

        var query = from chunk in _dbContext.DocumentChunks
                    join doc in _dbContext.Documents
                        on new { chunk.TenantId, Id = chunk.DocumentId }
                        equals new { doc.TenantId, doc.Id }
                    where chunk.TenantId == tenantId && chunk.Embedding != null
                    orderby chunk.Embedding!.CosineDistance(targetVector)
                    select new
                    {
                        ChunkId = chunk.Id,
                        DocumentId = doc.Id,
                        Sequence = chunk.Sequence,
                        Title = doc.Title,
                        SourceReference = doc.SourceReference,
                        Content = chunk.Content,
                        Distance = chunk.Embedding!.CosineDistance(targetVector)
                    };

        var rawResults = await query
            .Take(topK)
            .ToListAsync(cancellationToken);

        var items = new List<VectorSearchResultItem>(rawResults.Count);
        for (int i = 0; i < rawResults.Count; i++)
        {
            var res = rawResults[i];
            items.Add(new VectorSearchResultItem(
                res.ChunkId,
                res.DocumentId,
                res.Sequence,
                res.Title,
                res.SourceReference,
                res.Content,
                res.Distance,
                rank: i + 1));
        }

        return items;
    }
}
