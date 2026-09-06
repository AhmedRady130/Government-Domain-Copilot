using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;

/// <summary>
/// Infrastructure adapter for local Ollama alternative embedding provider.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    public const string Name = "Ollama";

    private readonly HttpClient _httpClient;
    private readonly EmbeddingProviderOptions _options;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    public string ProviderName => Name;

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        IOptions<EmbeddingProviderOptions> options,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Inputs.Count == 0)
        {
            throw new EmbeddingInvalidInputException("Cannot generate embeddings for an empty input list.");
        }

        var modelName = request.Model ?? _options.FallbackModel;
        var baseUrl = _options.OllamaBaseUrl.TrimEnd('/');
        var requestUri = $"{baseUrl}/api/embed";

        var payload = new OllamaBatchEmbedRequest(modelName, request.Inputs);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(requestUri, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Ollama API embedding request failed with HTTP {StatusCode}.", response.StatusCode);

                throw new EmbeddingProviderUnavailableException(
                    Name,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Details: {SanitizeErrorMessage(errorBody)}");
            }

            var responseData = await response.Content.ReadFromJsonAsync<OllamaBatchEmbedResponse>(
                cancellationToken: cancellationToken);

            stopwatch.Stop();

            if (responseData?.Embeddings == null || responseData.Embeddings.Count != request.Inputs.Count)
            {
                throw new EmbeddingProviderUnavailableException(
                    Name,
                    $"Returned embedding items count ({responseData?.Embeddings?.Count ?? 0}) does not match input count ({request.Inputs.Count}).");
            }

            var items = new List<EmbeddingItem>(responseData.Embeddings.Count);
            int dimension = 0;

            for (int i = 0; i < responseData.Embeddings.Count; i++)
            {
                var values = responseData.Embeddings[i];
                if (values == null || values.Count == 0)
                {
                    throw new EmbeddingProviderUnavailableException(Name, $"Ollama returned empty vector values for index {i}.");
                }

                dimension = values.Count;
                if (dimension != _options.ExpectedDimensions)
                {
                    throw new EmbeddingDimensionMismatchException(Name, _options.ExpectedDimensions, dimension);
                }

                items.Add(new EmbeddingItem(i, values));
            }

            _logger.LogInformation(
                "Successfully generated {Count} embeddings using provider {Provider} model {Model} in {DurationMs}ms.",
                items.Count,
                Name,
                modelName,
                stopwatch.ElapsedMilliseconds);

            return new EmbeddingResult(Name, modelName, dimension, items, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Ollama embedding HTTP transport error occurred.");
            throw new EmbeddingProviderUnavailableException(Name, "Network transport error communicating with local Ollama service.", ex);
        }
    }

    private static string SanitizeErrorMessage(string? errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
            return "No response body.";

        var clean = errorBody.Trim();
        if (clean.Length > 200)
        {
            clean = clean[..200] + "...";
        }
        return clean;
    }

    // --- Ollama DTOs ---

    private sealed record OllamaBatchEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record OllamaBatchEmbedResponse(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("embeddings")] IReadOnlyList<IReadOnlyList<float>>? Embeddings);
}
