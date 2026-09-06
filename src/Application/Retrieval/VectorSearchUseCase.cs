namespace GovernmentDomainCopilot.Application.Retrieval;

using System.Diagnostics;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging;

public sealed class VectorSearchUseCase : IVectorSearchUseCase
{
    private readonly ITenantContext _tenantContext;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChunkRetriever _chunkRetriever;
    private readonly ILogger<VectorSearchUseCase> _logger;

    public VectorSearchUseCase(
        ITenantContext tenantContext,
        IEmbeddingService embeddingService,
        IChunkRetriever chunkRetriever,
        ILogger<VectorSearchUseCase> logger)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _chunkRetriever = chunkRetriever ?? throw new ArgumentNullException(nameof(chunkRetriever));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VectorSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new VectorSearchValidationException("Search request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new VectorSearchValidationException("Search query cannot be empty or whitespace.");
        }

        int topK = request.TopK ?? VectorSearchLimits.DefaultTopK;
        if (topK <= 0)
        {
            throw new VectorSearchValidationException("TopK must be a positive integer.");
        }

        if (topK > VectorSearchLimits.MaxTopK)
        {
            topK = VectorSearchLimits.MaxTopK;
        }

        var tenantId = _tenantContext.GetTenantId();
        var stopwatch = Stopwatch.StartNew();

        EmbeddingResult embeddingResult;
        try
        {
            var embeddingRequest = new EmbeddingRequest([request.Query], tenantId: tenantId);
            embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(embeddingRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is not VectorSearchValidationException)
        {
            _logger.LogError(ex, "Failed to generate query embedding for tenant {TenantId}.", tenantId);
            throw new VectorSearchException("Failed to generate vector embedding for the search query.", ex);
        }

        if (embeddingResult == null || embeddingResult.Items.Count == 0)
        {
            throw new VectorSearchException("Embedding service returned an empty result.");
        }

        var queryVector = embeddingResult.Items[0].Vector;
        if (queryVector.Count != VectorSearchLimits.ExpectedDimension)
        {
            throw new VectorSearchException(
                $"Embedding dimension {queryVector.Count} does not match expected dimension {VectorSearchLimits.ExpectedDimension}.");
        }

        float[] vectorArray = queryVector is float[] arr ? arr : queryVector.ToArray();

        IReadOnlyList<VectorSearchResultItem> rawResults;
        try
        {
            rawResults = await _chunkRetriever.SearchVectorAsync(
                tenantId,
                vectorArray,
                topK,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed executing vector similarity query for tenant {TenantId}.", tenantId);
            throw new VectorSearchException("Vector database search query failed.", ex);
        }

        var rankedItems = new List<VectorSearchResultItem>(rawResults.Count);
        for (int i = 0; i < rawResults.Count; i++)
        {
            var item = rawResults[i];
            rankedItems.Add(new VectorSearchResultItem(
                item.ChunkId,
                item.DocumentId,
                item.Sequence,
                item.Title,
                item.SourceReference,
                item.Content,
                item.Distance,
                rank: i + 1));
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Vector search completed for tenant {TenantId}: TopK={TopK}, Returned={TotalReturned}, Provider={Provider}, Model={Model}, DurationMs={DurationMs}",
            tenantId,
            topK,
            rankedItems.Count,
            embeddingResult.ProviderName,
            embeddingResult.ModelName,
            stopwatch.ElapsedMilliseconds);

        return new VectorSearchResponse(
            topK,
            rankedItems.Count,
            stopwatch.Elapsed,
            embeddingResult.ProviderName,
            embeddingResult.ModelName,
            rankedItems);
    }
}
