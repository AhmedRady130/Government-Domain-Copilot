using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Answering.Exceptions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Infrastructure.LLM.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Tests.Answering;

public sealed class OllamaChatCompletionProviderTests
{
    private static readonly LlmProviderOptions Options = new()
    {
        PrimaryProvider = OllamaChatCompletionProvider.Name,
        PrimaryModel = "llama3.2",
        OllamaBaseUrl = "http://ollama.test:11434",
        DefaultTemperature = 0.2,
        DefaultMaxOutputTokens = 256,
        HttpTimeoutSeconds = 30
    };

    [Fact]
    public async Task CompleteAsync_MapsRequestAndSuccessfulResponse()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"model":"llama3.2","message":{"role":"assistant","content":"The fee is 5 demo credits [1]."},"done":true,"prompt_eval_count":12,"eval_count":8}""");
        var provider = CreateProvider(handler);

        var result = await provider.CompleteAsync(new ChatCompletionRequest("System rules", "What is the fee?"), CancellationToken.None);

        Assert.Equal("Ollama", result.ProviderName);
        Assert.Equal("llama3.2", result.ModelName);
        Assert.Equal("The fee is 5 demo credits [1].", result.Content);
        Assert.Equal(12, result.Usage?.PromptTokens);
        Assert.Equal(8, result.Usage?.CompletionTokens);
        Assert.Equal(20, result.Usage?.TotalTokens);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("llama3.2", json.RootElement.GetProperty("model").GetString());
        Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("system", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", json.RootElement.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal(256, json.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(0.2, json.RootElement.GetProperty("options").GetProperty("temperature").GetDouble(), 2);
        Assert.Equal("/api/chat", handler.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessResponse_IsUnavailableWithoutEchoingResponseBody()
    {
        var provider = CreateProvider(new FakeHandler(HttpStatusCode.InternalServerError, "do not expose provider details"));

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(() => provider.CompleteAsync(new ChatCompletionRequest("System", "Question"), CancellationToken.None));

        Assert.Equal("Ollama", ex.ProviderName);
        Assert.DoesNotContain("do not expose", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_Timeout_IsMappedToUnavailable()
    {
        var provider = CreateProvider(new ThrowingHandler(new TaskCanceledException("timeout")));

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(() => provider.CompleteAsync(new ChatCompletionRequest("System", "Question"), CancellationToken.None));

        Assert.Equal("Ollama", ex.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_Cancellation_Propagates()
    {
        var provider = CreateProvider(new DelayingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(new ChatCompletionRequest("System", "Question"), cancellation.Token));
    }

    [Fact]
    public async Task StreamCompleteAsync_MapsNdjsonAndYieldsIncrementalChunks()
    {
        var body = "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}\n" +
                   "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"world [1].\"},\"done\":true}\n";
        var handler = new FakeHandler(HttpStatusCode.OK, body);
        var provider = CreateProvider(handler);

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamCompleteAsync(new ChatCompletionRequest("System", "Question"), CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal(new[] { "Hello", "world [1]." }, chunks);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(json.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task StreamCompleteAsync_MalformedProviderResponse_IsUnavailable()
    {
        var provider = CreateProvider(new FakeHandler(HttpStatusCode.OK, "not-json\n"));

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(async () =>
        {
            await foreach (var _ in provider.StreamCompleteAsync(new ChatCompletionRequest("System", "Question"), CancellationToken.None)) { }
        });

        Assert.Equal("Ollama", ex.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_MissingMessageContent_IsUnavailable()
    {
        var provider = CreateProvider(new FakeHandler(HttpStatusCode.OK, """{"model":"llama3.2","done":true}"""));

        await Assert.ThrowsAsync<LlmProviderUnavailableException>(() => provider.CompleteAsync(new ChatCompletionRequest("System", "Question"), CancellationToken.None));
    }

    private static OllamaChatCompletionProvider CreateProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options), NullLogger<OllamaChatCompletionProvider>.Instance);

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
