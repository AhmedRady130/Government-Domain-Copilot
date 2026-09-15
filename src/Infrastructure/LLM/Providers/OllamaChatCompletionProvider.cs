namespace GovernmentDomainCopilot.Infrastructure.LLM.Providers;

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Exceptions;
using GovernmentDomainCopilot.Application.Answering.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Infrastructure adapter for a locally hosted Ollama chat-completion server.</summary>
public sealed class OllamaChatCompletionProvider : IChatCompletionProvider
{
    public const string Name = "Ollama";

    private readonly HttpClient _httpClient;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<OllamaChatCompletionProvider> _logger;

    public string ProviderName => Name;

    public OllamaChatCompletionProvider(
        HttpClient httpClient,
        IOptions<LlmProviderOptions> options,
        ILogger<OllamaChatCompletionProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.HttpTimeoutSeconds > 0)
        {
            try { _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds); }
            catch (InvalidOperationException) { }
        }
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (modelName, payload) = CreatePayload(request, stream: false);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatUri())
        {
            Content = JsonContent.Create(payload)
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            EnsureSuccess(response);
            var responseData = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseData?.Message?.Content))
                throw new LlmProviderUnavailableException(Name, "Ollama returned an empty completion message.");

            var usage = ToUsage(responseData);
            if (usage is not null) request.OnUsageResolved?.Invoke(usage);
            return new ChatCompletionResult(responseData.Message.Content, Name, responseData.Model ?? modelName, stopwatch.Elapsed, usage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Ollama completion HTTP transport error occurred.");
            throw new LlmProviderUnavailableException(Name, "Network transport error communicating with local Ollama service.", ex);
        }
        catch (JsonException ex)
        {
            throw new LlmProviderUnavailableException(Name, "Ollama returned an invalid completion response.", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamCompleteAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, payload) = CreatePayload(request, stream: true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatUri())
        {
            Content = JsonContent.Create(payload)
        };

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
            _logger.LogWarning(ex, "Ollama stream completion HTTP transport error occurred.");
            throw new LlmProviderUnavailableException(Name, "Network transport error communicating with local Ollama service.", ex);
        }

        using (response)
        {
            EnsureSuccess(response);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                OllamaChatResponse? chunk;
                try { chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line); }
                catch (JsonException ex)
                {
                    throw new LlmProviderUnavailableException(Name, "Ollama returned an invalid streaming completion response.", ex);
                }

                if (chunk is null)
                    throw new LlmProviderUnavailableException(Name, "Ollama returned an empty streaming completion response.");

                var usage = ToUsage(chunk);
                if (usage is not null) request.OnUsageResolved?.Invoke(usage);
                if (!string.IsNullOrEmpty(chunk.Message?.Content)) yield return chunk.Message.Content;
                if (chunk.Done) yield break;
            }
        }
    }

    private (string ModelName, OllamaChatRequest Payload) CreatePayload(ChatCompletionRequest request, bool stream)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            throw new ArgumentException("User prompt cannot be null or whitespace.", nameof(request));

        var modelName = request.Model ?? _options.PrimaryModel;
        if (string.IsNullOrWhiteSpace(modelName))
            throw new LlmProviderUnavailableException(Name, "No Ollama model is configured.");

        var temperature = request.Temperature ?? _options.DefaultTemperature;
        var maxTokens = request.MaxTokens ?? _options.DefaultMaxOutputTokens;
        if (temperature is < 0 or > 2 || maxTokens <= 0)
            throw new ArgumentException("Chat completion temperature or token limit is outside the configured safe range.", nameof(request));

        var messages = new List<OllamaMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) messages.Add(new OllamaMessage("system", request.SystemPrompt));
        messages.Add(new OllamaMessage("user", request.UserPrompt));
        return (modelName, new OllamaChatRequest(modelName, messages, stream, new OllamaOptions(temperature, maxTokens)));
    }

    private Uri BuildChatUri()
    {
        if (!Uri.TryCreate(_options.OllamaBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new LlmProviderUnavailableException(Name, "The configured Ollama base URL is invalid.");

        return new Uri(baseUri.ToString().TrimEnd('/') + "/api/chat");
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama completion request failed with HTTP {StatusCode}.", response.StatusCode);
            throw new LlmProviderUnavailableException(Name, $"Ollama returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static ChatCompletionUsageMetadata? ToUsage(OllamaChatResponse response)
    {
        if (response.PromptEvalCount is null && response.EvalCount is null) return null;
        var total = response.PromptEvalCount is null || response.EvalCount is null
            ? null
            : response.PromptEvalCount + response.EvalCount;
        return new ChatCompletionUsageMetadata(response.PromptEvalCount, response.EvalCount, total);
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaOptions Options);
    private sealed record OllamaMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content);
    private sealed record OllamaOptions([property: JsonPropertyName("temperature")] double Temperature, [property: JsonPropertyName("num_predict")] int NumPredict);
    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount = null,
        [property: JsonPropertyName("eval_count")] int? EvalCount = null);
}
