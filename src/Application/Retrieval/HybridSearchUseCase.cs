namespace GovernmentDomainCopilot.Application.Retrieval;

using System.Diagnostics;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Retrieval.Services;
using Microsoft.Extensions.Logging;

public sealed class HybridSearchUseCase : IHybridSearchUseCase
{
    private readonly ITenantContext _tenantContext;
    private readonly IVectorSearchUseCase _vectorSearchUseCase;
    private readonly IKeywordChunkRetriever _keywordRetriever;
    private readonly ReciprocalRankFusionService _rrfService;
    private readonly ILogger<HybridSearchUseCase> _logger;

    public HybridSearchUseCase(
        ITenantContext tenantContext,
        IVectorSearchUseCase vectorSearchUseCase,
        IKeywordChunkRetriever keywordRetriever,
        ReciprocalRankFusionService rrfService,
        ILogger<HybridSearchUseCase> logger)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _vectorSearchUseCase = vectorSearchUseCase ?? throw new ArgumentNullException(nameof(vectorSearchUseCase));
        _keywordRetriever = keywordRetriever ?? throw new ArgumentNullException(nameof(keywordRetriever));
        _rrfService = rrfService ?? throw new ArgumentNullException(nameof(rrfService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HybridSearchResponse> SearchAsync(
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

        int candidateTopK = Math.Min(topK * 2, VectorSearchLimits.MaxTopK);

        var tenantId = _tenantContext.GetTenantId();
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<VectorSearchResultItem> vectorCandidates = Array.Empty<VectorSearchResultItem>();
        IReadOnlyList<KeywordSearchResultItem> keywordCandidates = Array.Empty<KeywordSearchResultItem>();

        string providerName = "HybridRRF";
        string modelName = "pgvector+tsvector";

        bool vectorSuccess = false;
        bool keywordSuccess = false;

        try
        {
            var vectorRequest = new VectorSearchRequest(request.Query, candidateTopK);
            var vectorResponse = await _vectorSearchUseCase.SearchAsync(vectorRequest, cancellationToken);
            vectorCandidates = vectorResponse.Items;
            providerName = vectorResponse.ProviderName;
            modelName = vectorResponse.ModelName;
            vectorSuccess = true;
        }
        catch (Exception ex) when (ex is not VectorSearchValidationException)
        {
            _logger.LogWarning(ex, "Vector search branch failed for tenant {TenantId}. Degrading gracefully to keyword retrieval.", tenantId);
        }

        try
        {
            keywordCandidates = await _keywordRetriever.SearchKeywordAsync(tenantId, request.Query, candidateTopK, cancellationToken);
            keywordSuccess = true;
        }
        catch (Exception ex) when (ex is not VectorSearchValidationException)
        {
            _logger.LogWarning(ex, "Keyword search branch failed for tenant {TenantId}. Degrading gracefully to vector retrieval.", tenantId);
        }

        if (!vectorSuccess && !keywordSuccess)
        {
            _logger.LogError("Both vector and keyword search branches failed for tenant {TenantId}.", tenantId);
            throw new VectorSearchException("Both vector and keyword search branches failed during hybrid retrieval.");
        }

        var fused = _rrfService.Fuse(vectorCandidates, keywordCandidates, ReciprocalRankFusionService.DefaultK);
        var finalItems = fused.Take(topK).ToList();

        stopwatch.Stop();

        _logger.LogInformation(
            "Hybrid search completed for tenant {TenantId}: TopK={TopK}, VectorCandidates={VectorCandidates}, KeywordCandidates={KeywordCandidates}, TotalReturned={TotalReturned}, DurationMs={DurationMs}",
            tenantId,
            topK,
            vectorCandidates.Count,
            keywordCandidates.Count,
            finalItems.Count,
            stopwatch.ElapsedMilliseconds);

        return new HybridSearchResponse(
            topK,
            finalItems.Count,
            stopwatch.Elapsed,
            providerName,
            modelName,
            finalItems);
    }
}
