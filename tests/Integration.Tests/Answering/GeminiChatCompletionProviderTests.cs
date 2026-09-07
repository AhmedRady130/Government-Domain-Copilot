using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Answering.Exceptions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Infrastructure.LLM.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Tests.Answering;

public sealed class GeminiChatCompletionProviderTests
{
    private readonly LlmProviderOptions _options = new()
    {
        GeminiBaseUrl = "https://generativelanguage.googleapis.com",
        PrimaryModel = "gemini-2.5-flash",
        DefaultMaxOutputTokens = 1024,
        DefaultTemperature = 0.1,
        HttpTimeoutSeconds = 30
    };

    [Fact]
    public async Task CompleteAsync_SerializesCorrectGeminiJsonShape_WithSystemInstructionAndUserContent()
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = "Grounded response text [1]." } }
                    }
                }
            },
            usageMetadata = new
            {
                promptTokenCount = 15,
                candidatesTokenCount = 10,
                totalTokenCount = 25
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest(
            SystemPrompt: "CRITICAL: You are a government assistant. Rely only on evidence.",
            UserPrompt: "What is the filing fee?\n\nRETRIEVED EVIDENCE CHUNKS:\n[1] Fee is 500 EGP.");

        var result = await provider.CompleteAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Grounded response text [1].", result.Content);
        Assert.Equal("Gemini", result.ProviderName);
        Assert.Equal("gemini-2.5-flash", result.ModelName);

        // Assert outgoing JSON structure
        Assert.NotNull(httpHandler.LastRequestBody);
        using var doc = JsonDocument.Parse(httpHandler.LastRequestBody);
        var root = doc.RootElement;

        // 1. systemInstruction exists at root
        Assert.True(root.TryGetProperty("systemInstruction", out var systemInstructionElement),
            "systemInstruction must exist at root of outgoing JSON.");
        var systemParts = systemInstructionElement.GetProperty("parts");
        Assert.Equal(1, systemParts.GetArrayLength());
        Assert.Contains("CRITICAL: You are a government assistant", systemParts[0].GetProperty("text").GetString());

        // 2. contents[] contains only user role — system prompt is NOT inside contents[]
        Assert.True(root.TryGetProperty("contents", out var contentsElement));
        Assert.Equal(1, contentsElement.GetArrayLength());
        var userContent = contentsElement[0];
        Assert.Equal("user", userContent.GetProperty("role").GetString());
        var userText = userContent.GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Contains("What is the filing fee?", userText);
        Assert.Contains("[1] Fee is 500 EGP.", userText);
        Assert.DoesNotContain("system", userContent.GetProperty("role").GetString());

        // 3. generationConfig contains defaults from options
        Assert.True(root.TryGetProperty("generationConfig", out var genConfigElement));
        Assert.Equal(1024, genConfigElement.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0.1, genConfigElement.GetProperty("temperature").GetDouble(), precision: 2);

        // 4. Configured timeout applied to HttpClient
        Assert.Equal(TimeSpan.FromSeconds(30), httpClient.Timeout);

        // 5. API key is NOT in query string
        Assert.DoesNotContain("key=", httpHandler.LastRequestUri?.Query ?? string.Empty);
    }

    [Fact]
    public async Task CompleteAsync_CustomTemperatureAndMaxTokens_OverrideDefaults()
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = "Answer [1]." } }
                    }
                }
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest(
            SystemPrompt: "System instruction.",
            UserPrompt: "User prompt.",
            Temperature: 0.5,
            MaxTokens: 256);

        await provider.CompleteAsync(request, CancellationToken.None);

        Assert.NotNull(httpHandler.LastRequestBody);
        using var doc = JsonDocument.Parse(httpHandler.LastRequestBody);
        var root = doc.RootElement;
        var genConfigElement = root.GetProperty("generationConfig");

        Assert.Equal(256, genConfigElement.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0.5, genConfigElement.GetProperty("temperature").GetDouble(), precision: 2);
    }

    [Fact]
    public async Task CompleteAsync_Maps429ToLlmRateLimitException()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "Rate limit exceeded");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User query");
        var ex = await Assert.ThrowsAsync<LlmRateLimitException>(() => provider.CompleteAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_Maps500ToLlmProviderUnavailableException()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "Server error");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User query");
        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(() => provider.CompleteAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public string? LastRequestBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public HttpRequestHeaders? LastRequestHeaders { get; private set; }

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestHeaders = request.Headers;

            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
