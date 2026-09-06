using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Infrastructure.Embeddings.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Tests.Embeddings;

public sealed class GeminiEmbeddingProviderTests
{
    private readonly EmbeddingProviderOptions _options = new()
    {
        PrimaryProvider = "Gemini",
        PrimaryModel = "gemini-embedding-2",
        ExpectedDimensions = 768,
        GeminiBaseUrl = "https://generativelanguage.googleapis.com"
    };

    [Fact]
    public async Task EmbedAsync_maps_successful_gemini_json_response_to_typed_embedding_result()
    {
        var fakeVector = Enumerable.Repeat(0.123f, 768).ToList();
        var jsonResponse = JsonSerializer.Serialize(new
        {
            embeddings = new[]
            {
                new { values = fakeVector },
                new { values = fakeVector }
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<GeminiEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "First text", "Second text" });
        var result = await provider.EmbedAsync(request, CancellationToken.None);

        Assert.Equal("Gemini", result.ProviderName);
        Assert.Equal("gemini-embedding-2", result.ModelName);
        Assert.Equal(768, result.Dimension);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(768, result.Items[0].Vector.Count);
        Assert.Equal(0.123f, result.Items[0].Vector[0]);

        Assert.NotNull(httpHandler.LastRequestBody);
        // Correction 1: outputDimensionality must be inside embedContentConfig (not deprecated top-level field)
        Assert.Contains("embedContentConfig", httpHandler.LastRequestBody);
        Assert.Contains("outputDimensionality", httpHandler.LastRequestBody);
        Assert.Contains("768", httpHandler.LastRequestBody);
        Assert.Contains("models/gemini-embedding-2", httpHandler.LastRequestBody);
        // Correction 2: authentication via x-goog-api-key header (not ?key= query param)
        Assert.DoesNotContain("?key=", httpHandler.LastRequestUri?.Query ?? string.Empty);
    }

    [Fact]
    public async Task EmbedAsync_maps_429_rate_limit_to_EmbeddingRateLimitException()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "Rate limit exceeded");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<GeminiEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingRateLimitException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_maps_http_500_to_EmbeddingProviderUnavailableException()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<GeminiEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingProviderUnavailableException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
        Assert.Contains("HTTP 500", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_throws_dimension_mismatch_exception_when_gemini_returns_incompatible_dimensions()
    {
        var incompatibleVector = Enumerable.Repeat(0.5f, 512).ToList();
        var jsonResponse = JsonSerializer.Serialize(new
        {
            embeddings = new[]
            {
                new { values = incompatibleVector }
            }
        });

        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<GeminiEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
        Assert.Equal(768, ex.ExpectedDimension);
        Assert.Equal(512, ex.ActualDimension);
    }

    [Fact]
    public async Task EmbedAsync_does_not_leak_secrets_in_exception_messages()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "Invalid API key provided: secret_key_1234567890");
        var httpClient = new HttpClient(httpHandler);
        var provider = new GeminiEmbeddingProvider(httpClient, Options.Create(_options), NullLogger<GeminiEmbeddingProvider>.Instance);

        var request = new EmbeddingRequest(new[] { "Text" });

        var ex = await Assert.ThrowsAsync<EmbeddingProviderUnavailableException>(
            () => provider.EmbedAsync(request, CancellationToken.None));

        Assert.DoesNotContain("Authorization: Bearer", ex.Message);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseContent) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public HttpRequestHeaders? LastRequestHeaders { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestHeaders = request.Headers;

            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
