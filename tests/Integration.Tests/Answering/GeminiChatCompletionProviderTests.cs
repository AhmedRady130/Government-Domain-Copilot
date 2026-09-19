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

    // -----------------------------------------------------------------------
    // StreamCompleteAsync tests (H-2)
    // -----------------------------------------------------------------------

    /// <summary>Drains an <see cref="IAsyncEnumerable{T}"/> into a list (avoids System.Linq.Async dependency).</summary>
    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> source, CancellationToken ct = default)
    {
        var list = new List<string>();
        await foreach (var item in source.WithCancellation(ct))
            list.Add(item);
        return list;
    }

    /// <summary>Builds a valid SSE body with one data line per chunk, terminated by [DONE].</summary>
    private static string BuildSseBody(IEnumerable<string> textChunks)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var text in textChunks)
        {
            var json = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text } } } }
                }
            });
            sb.Append("data: ").Append(json).Append("\n\n");
        }
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }


    [Fact]
    public async Task StreamCompleteAsync_RequestUri_ContainsStreamGenerateContentAndAltSse()
    {
        var body = BuildSseBody(new[] { "Hello" });
        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.OK, body);
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");
        _ = await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None));

        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains(":streamGenerateContent", handler.LastRequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("alt=sse", handler.LastRequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamCompleteAsync_ApiKey_SentAsHeaderNotQueryString()
    {
        var prevKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-gemini-api-key");

            var body = BuildSseBody(new[] { "Hello" });
            var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.OK, body);
            var httpClient = new HttpClient(handler);
            var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

            var request = new ChatCompletionRequest("System", "User");
            _ = await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None));

            // API key must NOT appear in the query string.
            Assert.DoesNotContain("key=", handler.LastRequestUri?.Query ?? string.Empty, StringComparison.Ordinal);

            // API key header must be present.
            Assert.NotNull(handler.LastRequestHeaders);
            Assert.True(handler.LastRequestHeaders!.Contains("x-goog-api-key"),
                "Expected x-goog-api-key header on the streaming request.");
            Assert.Equal("test-gemini-api-key", handler.LastRequestHeaders.GetValues("x-goog-api-key").First());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", prevKey);
        }
    }

    [Fact]
    public async Task StreamCompleteAsync_MultipleDataLines_YieldsIncrementalChunks()
    {
        var expectedChunks = new[] { "Hello", " world", "!" };
        var body = BuildSseBody(expectedChunks);
        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.OK, body);
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");
        var chunks = await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None));

        // Each SSE data line must produce exactly one yielded chunk — not batched.
        Assert.Equal(expectedChunks.Length, chunks.Count);
        Assert.Equal(expectedChunks, chunks);
    }

    [Fact]
    public async Task StreamCompleteAsync_DoneTerminator_EndsIterationCleanly()
    {
        // Body with two chunks followed by [DONE]; iteration must stop after [DONE].
        var body = BuildSseBody(new[] { "First", "Second" });
        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.OK, body);
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");
        var chunks = await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("First", chunks[0]);
        Assert.Equal("Second", chunks[1]);
    }

    [Fact]
    public async Task StreamCompleteAsync_Http429_ThrowsLlmRateLimitException()
    {
        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "Rate limit exceeded");
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");
        var ex = await Assert.ThrowsAsync<LlmRateLimitException>(async () =>
            await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None)));

        Assert.Equal("Gemini", ex.ProviderName);
    }

    [Fact]
    public async Task StreamCompleteAsync_MalformedAndEmptySseLines_DoNotCrashParser()
    {
        // Mix of empty lines, comment lines, malformed JSON, and a valid chunk.
        var sseBody =
            "\n" +
            ": comment line\n" +
            "data: {not valid json}\n\n" +
            "data: \n\n" +
            "data: " + JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = "OK" } } } }
                }
            }) + "\n\n" +
            "data: [DONE]\n\n";

        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.OK, sseBody);
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");
        // Must not throw; valid chunk must still be yielded.
        var chunks = await DrainAsync(provider.StreamCompleteAsync(request, CancellationToken.None));

        Assert.Contains("OK", chunks);
    }

    [Fact]
    public async Task StreamCompleteAsync_CancellationToken_PropagatesAndThrowsOperationCancelled()
    {
        // Use a TCS-backed handler that blocks until cancellation is signalled.
        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        var handler = new BlockingFakeHttpMessageHandler(tcs.Task);
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var request = new ChatCompletionRequest("System", "User");

        // Cancel after a short delay.
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DrainAsync(provider.StreamCompleteAsync(request, cts.Token), cts.Token));
    }

    [Fact]
    public async Task CompleteAsync_non_success_response_does_not_echo_response_body()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "Invalid API key provided: FAKE-TEST-VALUE");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(
            () => provider.CompleteAsync(new ChatCompletionRequest("System", "User query"), CancellationToken.None));

        Assert.Contains("403", ex.Message);
        Assert.DoesNotContain("FAKE-TEST-VALUE", ex.Message);
    }

    [Fact]
    public async Task StreamCompleteAsync_non_success_response_does_not_echo_response_body()
    {
        var handler = new StreamingFakeHttpMessageHandler(HttpStatusCode.Forbidden, "Invalid API key provided: FAKE-TEST-VALUE");
        var httpClient = new HttpClient(handler);
        var provider = new GeminiChatCompletionProvider(httpClient, Options.Create(_options), NullLogger<GeminiChatCompletionProvider>.Instance);

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(async () =>
            await DrainAsync(provider.StreamCompleteAsync(new ChatCompletionRequest("System", "User"), CancellationToken.None)));

        Assert.Contains("403", ex.Message);
        Assert.DoesNotContain("FAKE-TEST-VALUE", ex.Message);
    }
    private sealed class StreamingFakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public Uri? LastRequestUri { get; private set; }
        public HttpRequestHeaders? LastRequestHeaders { get; private set; }

        public StreamingFakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestHeaders = request.Headers;

            var bytes = System.Text.Encoding.UTF8.GetBytes(_responseBody);
            var stream = new System.IO.MemoryStream(bytes);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

            var response = new HttpResponseMessage(_statusCode) { Content = content };
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingFakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Task<HttpResponseMessage> _responseTask;

        public BlockingFakeHttpMessageHandler(Task<HttpResponseMessage> responseTask)
            => _responseTask = responseTask;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responseTask;
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
