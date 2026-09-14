namespace GovernmentDomainCopilot.Infrastructure.LLM.Providers;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Exceptions;
using GovernmentDomainCopilot.Application.Answering.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class GeminiChatCompletionProvider : IChatCompletionProvider
{
    public const string Name = "Gemini";

    private readonly HttpClient _httpClient;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<GeminiChatCompletionProvider> _logger;

    public string ProviderName => Name;

    public GeminiChatCompletionProvider(
        HttpClient httpClient,
        IOptions<LlmProviderOptions> options,
        ILogger<GeminiChatCompletionProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.HttpTimeoutSeconds > 0)
        {
            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
            }
            catch (InvalidOperationException)
            {
                // HttpClient timeout can only be set before sending requests
            }
        }
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            throw new ArgumentException("User prompt cannot be null or whitespace.", nameof(request));
        }

        var modelName = request.Model ?? _options.PrimaryModel;
        var formattedModel = modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? modelName
            : $"models/{modelName}";

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var baseUrl = _options.GeminiBaseUrl.TrimEnd('/');
        var requestUri = $"{baseUrl}/v1beta/{formattedModel}:generateContent";

        GeminiSystemInstruction? systemInstruction = null;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            systemInstruction = new GeminiSystemInstruction(new[] { new GeminiPartItem(request.SystemPrompt) });
        }

        var contents = new List<GeminiContentItem>
        {
            new GeminiContentItem("user", new[] { new GeminiPartItem(request.UserPrompt) })
        };

        var temperature = request.Temperature ?? _options.DefaultTemperature;
        var maxTokens = request.MaxTokens ?? _options.DefaultMaxOutputTokens;
        var generationConfig = new GeminiGenerationConfig(temperature, maxTokens);

        var payload = new GeminiGenerateContentRequest(
            contents,
            systemInstruction,
            generationConfig);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequest.Content = JsonContent.Create(payload);

        // MANDATORY REQUIREMENT: Use x-goog-api-key header. Do NOT put API key in query string.
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
                throw new LlmRateLimitException(Name, "Gemini API rate limit exceeded (HTTP 429).");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Gemini completion request failed with HTTP {StatusCode}.", response.StatusCode);

                throw new LlmProviderUnavailableException(
                    Name,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Details: {SanitizeErrorMessage(errorBody)}");
            }

            var responseData = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(
                cancellationToken: cancellationToken);

            stopwatch.Stop();

            var candidate = responseData?.Candidates?.FirstOrDefault();
            var part = candidate?.Content?.Parts?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(part?.Text))
            {
                throw new LlmProviderUnavailableException(Name, "Gemini API returned empty completion text.");
            }

            ChatCompletionUsageMetadata? usage = null;
            if (responseData?.UsageMetadata != null)
            {
                usage = new ChatCompletionUsageMetadata(
                    responseData.UsageMetadata.PromptTokenCount,
                    responseData.UsageMetadata.CandidatesTokenCount,
                    responseData.UsageMetadata.TotalTokenCount);
            }

            return new ChatCompletionResult(
                part.Text,
                Name,
                modelName,
                stopwatch.Elapsed,
                usage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Gemini completion HTTP transport error occurred.");
            throw new LlmProviderUnavailableException(Name, "Network transport error communicating with Gemini API.", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamCompleteAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            throw new ArgumentException("User prompt cannot be null or whitespace.", nameof(request));
        }

        var modelName = request.Model ?? _options.PrimaryModel;
        var formattedModel = modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? modelName
            : $"models/{modelName}";

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var baseUrl = _options.GeminiBaseUrl.TrimEnd('/');
        var requestUri = $"{baseUrl}/v1beta/{formattedModel}:streamGenerateContent?alt=sse";

        GeminiSystemInstruction? systemInstruction = null;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            systemInstruction = new GeminiSystemInstruction(new[] { new GeminiPartItem(request.SystemPrompt) });
        }

        var contents = new List<GeminiContentItem>
        {
            new GeminiContentItem("user", new[] { new GeminiPartItem(request.UserPrompt) })
        };

        var temperature = request.Temperature ?? _options.DefaultTemperature;
        var maxTokens = request.MaxTokens ?? _options.DefaultMaxOutputTokens;
        var generationConfig = new GeminiGenerationConfig(temperature, maxTokens);

        var payload = new GeminiGenerateContentRequest(
            contents,
            systemInstruction,
            generationConfig);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequest.Content = JsonContent.Create(payload);

        // MANDATORY REQUIREMENT: Use x-goog-api-key header. Do NOT put API key in query string.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Gemini stream completion HTTP transport error occurred.");
            throw new LlmProviderUnavailableException(Name, "Network transport error communicating with Gemini API.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new LlmRateLimitException(Name, "Gemini API rate limit exceeded (HTTP 429).");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Gemini stream completion request failed with HTTP {StatusCode}.", response.StatusCode);

                throw new LlmProviderUnavailableException(
                    Name,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Details: {SanitizeErrorMessage(errorBody)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                {
                    var json = line.Substring(6).Trim();
                    if (json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    GeminiGenerateContentResponse? chunkObj = null;
                    try
                    {
                        chunkObj = System.Text.Json.JsonSerializer.Deserialize<GeminiGenerateContentResponse>(json);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        continue;
                    }

                    var chunkText = chunkObj?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                    if (!string.IsNullOrEmpty(chunkText))
                    {
                        yield return chunkText;
                    }
                }
            }
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

    // --- Gemini Chat DTOs ---

    private sealed record GeminiGenerateContentRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContentItem> Contents,
        [property: JsonPropertyName("systemInstruction")] GeminiSystemInstruction? SystemInstruction = null,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig? GenerationConfig = null);

    private sealed record GeminiSystemInstruction(
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPartItem> Parts);

    private sealed record GeminiContentItem(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPartItem> Parts);

    private sealed record GeminiPartItem(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("temperature")] double? Temperature = null,
        [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens = null);

    private sealed record GeminiGenerateContentResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadataResponse? UsageMetadata = null);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiResponseContent? Content);

    private sealed record GeminiResponseContent(
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPartItem>? Parts);

    private sealed record GeminiUsageMetadataResponse(
        [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount,
        [property: JsonPropertyName("totalTokenCount")] int? TotalTokenCount);
}
