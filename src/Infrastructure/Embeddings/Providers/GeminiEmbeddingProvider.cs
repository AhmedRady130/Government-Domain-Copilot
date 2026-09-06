using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;

/// <summary>
/// Infrastructure adapter for Google Gemini API hosted embedding provider.
/// </summary>
public sealed class GeminiEmbeddingProvider : IEmbeddingProvider
{
    public const string Name = "Gemini";

    private readonly HttpClient _httpClient;
    private readonly EmbeddingProviderOptions _options;
    private readonly ILogger<GeminiEmbeddingProvider> _logger;

    public string ProviderName => Name;

    public GeminiEmbeddingProvider(
        HttpClient httpClient,
        IOptions<EmbeddingProviderOptions> options,
        ILogger<GeminiEmbeddingProvider> logger)
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

        var modelName = request.Model ?? _options.PrimaryModel;
        var formattedModel = modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? modelName
            : $"models/{modelName}";

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var baseUrl = _options.GeminiBaseUrl.TrimEnd('/');
        var requestUri = $"{baseUrl}/v1beta/{formattedModel}:batchEmbedContents";

        var embedConfig = new GeminiEmbedContentConfig(_options.ExpectedDimensions);

        var payload = new GeminiBatchEmbedRequest(
            request.Inputs.Select(input => new GeminiEmbedRequestItem(
                formattedModel,
                new GeminiContent(new[] { new GeminiPart(input) }),
                embedConfig)
            ).ToList()
        );

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequest.Content = JsonContent.Create(payload);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new EmbeddingRateLimitException(Name, "Gemini API rate limit exceeded (HTTP 429).");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Gemini API embedding request failed with HTTP {StatusCode}.", response.StatusCode);

                throw new EmbeddingProviderUnavailableException(
                    Name,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Details: {SanitizeErrorMessage(errorBody)}");
            }

            var responseData = await response.Content.ReadFromJsonAsync<GeminiBatchEmbedResponse>(
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
                var values = responseData.Embeddings[i].Values;
                if (values == null || values.Count == 0)
                {
                    throw new EmbeddingProviderUnavailableException(Name, $"Gemini returned empty vector values for index {i}.");
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
            _logger.LogWarning(ex, "Gemini embedding HTTP transport error occurred.");
            throw new EmbeddingProviderUnavailableException(Name, "Network transport error communicating with Gemini API.", ex);
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

    // --- Gemini DTOs ---

    private sealed record GeminiBatchEmbedRequest(
        [property: JsonPropertyName("requests")] IReadOnlyList<GeminiEmbedRequestItem> Requests);

    private sealed record GeminiEmbedRequestItem(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] GeminiContent Content,
        [property: JsonPropertyName("embedContentConfig")] GeminiEmbedContentConfig? EmbedContentConfig = null);

    private sealed record GeminiEmbedContentConfig(
        [property: JsonPropertyName("outputDimensionality")] int OutputDimensionality);

    private sealed record GeminiContent(
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GeminiBatchEmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<GeminiEmbeddingValues>? Embeddings);

    private sealed record GeminiEmbeddingValues(
        [property: JsonPropertyName("values")] IReadOnlyList<float>? Values);
}
