using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Application.Embeddings;

/// <summary>
/// High-level application service that orchestrates embedding generation with primary and fallback provider routing.
/// </summary>
public sealed class ResilientEmbeddingService : IEmbeddingService
{
    private readonly IReadOnlyDictionary<string, IEmbeddingProvider> _providers;
    private readonly EmbeddingProviderOptions _options;
    private readonly ILogger<ResilientEmbeddingService> _logger;

    public ResilientEmbeddingService(
        IEnumerable<IEmbeddingProvider> providers,
        IOptions<EmbeddingProviderOptions> options,
        ILogger<ResilientEmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        _providers = providers.ToDictionary(
            p => p.ProviderName,
            p => p,
            StringComparer.OrdinalIgnoreCase);

        if (_options.ExpectedDimensions <= 0)
        {
            throw new EmbeddingConfigurationException("ExpectedDimensions must be greater than zero.");
        }

        if (_options.MaxBatchSize <= 0)
        {
            throw new EmbeddingConfigurationException("MaxBatchSize must be greater than zero.");
        }
    }

    public async Task<EmbeddingResult> GenerateEmbeddingsAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Inputs.Count > _options.MaxBatchSize)
        {
            throw new EmbeddingInvalidInputException(
                $"Embedding request input count ({request.Inputs.Count}) exceeds maximum allowed batch size ({_options.MaxBatchSize}).");
        }

        var primaryProviderName = _options.PrimaryProvider;
        if (!_providers.TryGetValue(primaryProviderName, out var primaryProvider))
        {
            throw new EmbeddingConfigurationException(
                $"Configured primary embedding provider '{primaryProviderName}' is not registered.");
        }

        var primaryModel = request.Model ?? _options.PrimaryModel;
        var primaryRequest = new EmbeddingRequest(request.Inputs, primaryModel, request.TenantId);

        try
        {
            var result = await primaryProvider.EmbedAsync(primaryRequest, cancellationToken);
            ValidateDimensions(result, primaryProviderName);
            return result;
        }
        catch (Exception ex) when (ex is EmbeddingProviderUnavailableException or EmbeddingRateLimitException)
        {
            _logger.LogWarning(
                ex,
                "Primary embedding provider {PrimaryProvider} failed due to availability/transient issue. Evaluating fallback options.",
                primaryProviderName);

            var fallbackProviderName = _options.FallbackProvider;
            if (string.IsNullOrWhiteSpace(fallbackProviderName) ||
                fallbackProviderName.Equals(primaryProviderName, StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            if (!_providers.TryGetValue(fallbackProviderName, out var fallbackProvider))
            {
                _logger.LogError(
                    "Configured fallback embedding provider {FallbackProvider} is not registered.",
                    fallbackProviderName);

                throw new EmbeddingConfigurationException(
                    $"Configured fallback embedding provider '{fallbackProviderName}' is not registered.");
            }

            _logger.LogInformation(
                "Routing embedding request to fallback provider {FallbackProvider} with model {FallbackModel}.",
                fallbackProviderName,
                _options.FallbackModel);

            var fallbackRequest = new EmbeddingRequest(request.Inputs, _options.FallbackModel, request.TenantId);
            var fallbackResult = await fallbackProvider.EmbedAsync(fallbackRequest, cancellationToken);

            ValidateDimensions(fallbackResult, fallbackProviderName);
            return fallbackResult;
        }
    }

    private void ValidateDimensions(EmbeddingResult result, string providerName)
    {
        foreach (var item in result.Items)
        {
            if (item.Vector.Count != _options.ExpectedDimensions)
            {
                throw new EmbeddingDimensionMismatchException(
                    providerName,
                    _options.ExpectedDimensions,
                    item.Vector.Count);
            }
        }
    }
}
