using System.Net;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Tests.Embeddings;

public sealed class OllamaEmbeddingProviderTests
{
    private readonly EmbeddingProviderOptions _options = new()
    {
        FallbackProvider = "Ollama",
        FallbackModel = "nomic-embed-text",
        ExpectedDimensions = 768,
        OllamaBaseUrl = "http://localhost:11434"
    };

    [Fact]
    public async Task EmbedAsync_maps_successful_ollama_json_response_to_typed_embedding_result()
    {
        var fakeVector = Enumerable.Repeat(0.456f, 768).ToList();
        var jsonResponse = JsonSerializer.Serialize(new
        {
            model = "nomic-embed-text",
            embeddings = new[]
            {
                fakeVector,
                fakeVector
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<OllamaEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "First text", "Second text" });
        var result = await provider.EmbedAsync(request, CancellationToken.None);

        Assert.Equal("Ollama", result.ProviderName);
        Assert.Equal("nomic-embed-text", result.ModelName);
        Assert.Equal(768, result.Dimension);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(768, result.Items[0].Vector.Count);
        Assert.Equal(0.456f, result.Items[0].Vector[0]);
    }

    [Fact]
    public async Task EmbedAsync_maps_connection_failure_to_EmbeddingProviderUnavailableException()
    {
        var httpHandler = new FakeFailingHttpMessageHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(httpHandler);
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<OllamaEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingProviderUnavailableException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.Equal("Ollama", ex.ProviderName);
        Assert.Contains("Network transport error", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_throws_dimension_mismatch_exception_when_ollama_returns_incompatible_dimensions()
    {
        var incompatibleVector = Enumerable.Repeat(0.99f, 384).ToList();
        var jsonResponse = JsonSerializer.Serialize(new
        {
            model = "nomic-embed-text",
            embeddings = new[]
            {
                incompatibleVector
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<OllamaEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.Equal("Ollama", ex.ProviderName);
        Assert.Equal(768, ex.ExpectedDimension);
        Assert.Equal(384, ex.ActualDimension);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeFailingHttpMessageHandler(Exception exceptionToThrow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw exceptionToThrow;
        }
    }
}
