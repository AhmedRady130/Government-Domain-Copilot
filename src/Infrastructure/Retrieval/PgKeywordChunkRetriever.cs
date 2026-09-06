namespace GovernmentDomainCopilot.Infrastructure.Retrieval;

using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

public sealed class PgKeywordChunkRetriever : IKeywordChunkRetriever
{
    private readonly GovernmentDomainCopilotDbContext _dbContext;

    public PgKeywordChunkRetriever(GovernmentDomainCopilotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
        Guid tenantId,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (topK <= 0)
        {
            throw new ArgumentException("TopK must be positive.", nameof(topK));
        }

        if (!_dbContext.Database.IsNpgsql())
        {
            return await SearchInMemoryFallbackAsync(tenantId, query, topK, cancellationToken);
        }

        var tsQuery = EF.Functions.WebSearchToTsQuery("simple", query);

        var dbQuery = from chunk in _dbContext.DocumentChunks
                      join doc in _dbContext.Documents
                          on new { chunk.TenantId, Id = chunk.DocumentId }
                          equals new { doc.TenantId, doc.Id }
                      where chunk.TenantId == tenantId && EF.Property<NpgsqlTsVector>(chunk, "SearchVector").Matches(tsQuery)
                      orderby EF.Property<NpgsqlTsVector>(chunk, "SearchVector").Rank(tsQuery) descending
                      select new
                      {
                          ChunkId = chunk.Id,
                          DocumentId = doc.Id,
                          Sequence = chunk.Sequence,
                          Title = doc.Title,
                          SourceReference = doc.SourceReference,
                          Content = chunk.Content,
                          Score = (double)EF.Property<NpgsqlTsVector>(chunk, "SearchVector").Rank(tsQuery)
                      };

        var rawResults = await dbQuery
            .Take(topK)
            .ToListAsync(cancellationToken);

        var items = new List<KeywordSearchResultItem>(rawResults.Count);
        for (int i = 0; i < rawResults.Count; i++)
        {
            var res = rawResults[i];
            items.Add(new KeywordSearchResultItem(
                res.ChunkId,
                res.DocumentId,
                res.Sequence,
                res.Title,
                res.SourceReference,
                res.Content,
                res.Score,
                Rank: i + 1));
        }

        return items;
    }

    private async Task<IReadOnlyList<KeywordSearchResultItem>> SearchInMemoryFallbackAsync(
        Guid tenantId,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var dbQuery = from chunk in _dbContext.DocumentChunks
                      join doc in _dbContext.Documents
                          on new { chunk.TenantId, Id = chunk.DocumentId }
                          equals new { doc.TenantId, doc.Id }
                      where chunk.TenantId == tenantId
                      select new
                      {
                          ChunkId = chunk.Id,
                          DocumentId = doc.Id,
                          Sequence = chunk.Sequence,
                          Title = doc.Title,
                          SourceReference = doc.SourceReference,
                          Content = chunk.Content
                      };

        var allChunks = await dbQuery.ToListAsync(cancellationToken);
        var matches = new List<(KeywordSearchResultItem Item, double Score)>();

        foreach (var c in allChunks)
        {
            int matchCount = 0;
            foreach (var term in terms)
            {
                if (c.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                }
            }

            if (matchCount > 0)
            {
                double score = (double)matchCount / terms.Length;
                matches.Add((new KeywordSearchResultItem(
                    c.ChunkId, c.DocumentId, c.Sequence, c.Title, c.SourceReference, c.Content, score, 0), score));
            }
        }

        var sorted = matches.OrderByDescending(x => x.Score).Take(topK).ToList();
        var items = new List<KeywordSearchResultItem>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var m = sorted[i].Item;
            items.Add(new KeywordSearchResultItem(
                m.ChunkId, m.DocumentId, m.Sequence, m.Title, m.SourceReference, m.Content, m.KeywordScore, Rank: i + 1));
        }

        return items;
    }
}
