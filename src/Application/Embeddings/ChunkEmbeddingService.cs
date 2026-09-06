using GovernmentDomainCopilot.Application.Documents.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Application.Embeddings;

/// <summary>
/// Default implementation of <see cref="IChunkEmbeddingService"/>.
/// Generates embeddings via <see cref="IEmbeddingService"/> and persists them via <see cref="IChunkEmbeddingRepository"/>.
/// </summary>
public sealed class ChunkEmbeddingService : IChunkEmbeddingService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IChunkEmbeddingRepository _chunkEmbeddingRepository;
    private readonly EmbeddingProviderOptions _options;
    private readonly ILogger<ChunkEmbeddingService> _logger;

    public ChunkEmbeddingService(
        IEmbeddingService embeddingService,
        IChunkEmbeddingRepository chunkEmbeddingRepository,
        IOptions<EmbeddingProviderOptions> options,
        ILogger<ChunkEmbeddingService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _chunkEmbeddingRepository = chunkEmbeddingRepository ?? throw new ArgumentNullException(nameof(chunkEmbeddingRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EmbedAndPersistChunksAsync(
        Guid tenantId,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "TenantId cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
        {
            throw new ArgumentException("Chunks list cannot be empty.", nameof(chunks));
        }

        // Multi-tenancy guard: check all chunks belong to tenantId
        if (chunks.Any(c => c.TenantId != tenantId))
        {
            throw new InvalidOperationException("Cross-tenant chunk embedding attempt detected. All chunks must belong to tenantId.");
        }

        var inputs = chunks.Select(c => c.Content).ToList();
        var request = new EmbeddingRequest(inputs, tenantId: tenantId);

        var result = await _embeddingService.GenerateEmbeddingsAsync(request, cancellationToken);

        if (result.Dimension != _options.ExpectedDimensions)
        {
            throw new InvalidOperationException(
                $"Generated embedding dimension ({result.Dimension}) does not match expected dimension ({_options.ExpectedDimensions}).");
        }

        // Match generated vectors to chunks by Index position
        var itemsByIndex = result.Items.ToDictionary(item => item.Index, item => item.Vector.ToArray());
        var embeddingsToPersist = new List<(Guid ChunkId, float[] Vector)>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!itemsByIndex.TryGetValue(i, out var vector))
            {
                throw new InvalidOperationException($"Missing embedding vector for chunk at index {i}.");
            }

            embeddingsToPersist.Add((chunks[i].Id, vector));
        }

        await _chunkEmbeddingRepository.PersistEmbeddingsAsync(
            tenantId,
            embeddingsToPersist,
            _options.ExpectedDimensions,
            cancellationToken);

        _logger.LogInformation(
            "Persisted embeddings for {ChunkCount} chunks. TenantId: {TenantId}, Provider: {ProviderName}, Model: {ModelName}, Dimension: {Dimension}, DurationMs: {DurationMs}",
            chunks.Count,
            tenantId,
            result.ProviderName,
            result.ModelName,
            result.Dimension,
            result.Duration.TotalMilliseconds);
    }
}
