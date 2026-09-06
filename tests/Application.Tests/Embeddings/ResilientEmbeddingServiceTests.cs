using GovernmentDomainCopilot.Application.Embeddings;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Exceptions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Application.Tests.Embeddings;

public sealed class ResilientEmbeddingServiceTests
{
    private readonly EmbeddingProviderOptions _options = new()
    {
        PrimaryProvider = "Gemini",
        FallbackProvider = "Ollama",
        PrimaryModel = "gemini-embedding-2",
        FallbackModel = "nomic-embed-text",
        ExpectedDimensions = 768,
        MaxBatchSize = 100
    };

    [Fact]
    public async Task GenerateEmbeddingsAsync_uses_primary_provider_when_available()
    {
        var primaryProvider = new FakeEmbeddingProvider("Gemini", 768);
        var fallbackProvider = new FakeEmbeddingProvider("Ollama", 768);
        var service = CreateService(primaryProvider, fallbackProvider);

        var request = new EmbeddingRequest(new[] { "Test text for embedding" });
        var result = await service.GenerateEmbeddingsAsync(request, CancellationToken.None);

        Assert.Equal("Gemini", result.ProviderName);
        Assert.Equal("gemini-embedding-2", result.ModelName);
        Assert.Equal(768, result.Dimension);
        Assert.Single(result.Items);
        Assert.Equal(1, primaryProvider.CallCount);
        Assert.Equal(0, fallbackProvider.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_falls_back_to_secondary_provider_on_transient_failure()
    {
        var failingPrimary = new FakeFailingEmbeddingProvider("Gemini", new EmbeddingProviderUnavailableException("Gemini", "Service down"));
        var fallbackProvider = new FakeEmbeddingProvider("Ollama", 768);
        var service = CreateService(failingPrimary, fallbackProvider);

        var request = new EmbeddingRequest(new[] { "Test text for embedding" });
        var result = await service.GenerateEmbeddingsAsync(request, CancellationToken.None);

        Assert.Equal("Ollama", result.ProviderName);
        Assert.Equal("nomic-embed-text", result.ModelName);
        Assert.Equal(768, result.Dimension);
        Assert.Equal(1, failingPrimary.CallCount);
        Assert.Equal(1, fallbackProvider.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_throws_dimension_mismatch_exception_when_vector_dimension_invalid()
    {
        var invalidDimensionProvider = new FakeEmbeddingProvider("Gemini", 512);
        var service = CreateService(invalidDimensionProvider);

        var request = new EmbeddingRequest(new[] { "Test text" });

        var ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => service.GenerateEmbeddingsAsync(request, CancellationToken.None));

        Assert.Equal("Gemini", ex.ProviderName);
        Assert.Equal(768, ex.ExpectedDimension);
        Assert.Equal(512, ex.ActualDimension);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_throws_invalid_input_exception_on_exceeding_max_batch_size()
    {
        var primaryProvider = new FakeEmbeddingProvider("Gemini", 768);
        var service = CreateService(primaryProvider);

        var oversizedInputs = Enumerable.Range(0, 101).Select(i => $"Text {i}").ToList();
        var request = new EmbeddingRequest(oversizedInputs);

        var ex = await Assert.ThrowsAsync<EmbeddingInvalidInputException>(
            () => service.GenerateEmbeddingsAsync(request, CancellationToken.None));

        Assert.Contains("exceeds maximum allowed batch size", ex.Message);
        Assert.Equal(0, primaryProvider.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_does_not_fallback_on_invalid_input_exception()
    {
        var invalidInputPrimary = new FakeFailingEmbeddingProvider("Gemini", new EmbeddingInvalidInputException("Bad text"));
        var fallbackProvider = new FakeEmbeddingProvider("Ollama", 768);
        var service = CreateService(invalidInputPrimary, fallbackProvider);

        var request = new EmbeddingRequest(new[] { "Text" });

        await Assert.ThrowsAsync<EmbeddingInvalidInputException>(
            () => service.GenerateEmbeddingsAsync(request, CancellationToken.None));

        Assert.Equal(1, invalidInputPrimary.CallCount);
        Assert.Equal(0, fallbackProvider.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_respects_cancellation_token()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var primaryProvider = new FakeEmbeddingProvider("Gemini", 768);
        var service = CreateService(primaryProvider);

        var request = new EmbeddingRequest(new[] { "Text" });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GenerateEmbeddingsAsync(request, cts.Token));
    }

    private ResilientEmbeddingService CreateService(params IEmbeddingProvider[] providers)
    {
        return new ResilientEmbeddingService(
            providers,
            Options.Create(_options),
            NullLogger<ResilientEmbeddingService>.Instance);
    }

    private sealed class FakeEmbeddingProvider(string providerName, int dimension) : IEmbeddingProvider
    {
        public string ProviderName => providerName;
        public int CallCount { get; private set; }

        public Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            var items = request.Inputs.Select((text, index) =>
                new EmbeddingItem(index, Enumerable.Repeat(0.1f, dimension).ToList())
            ).ToList();

            var result = new EmbeddingResult(providerName, request.Model ?? "default-model", dimension, items, TimeSpan.FromMilliseconds(10));
            return Task.FromResult(result);
        }
    }

    private sealed class FakeFailingEmbeddingProvider(string providerName, Exception exceptionToThrow) : IEmbeddingProvider
    {
        public string ProviderName => providerName;
        public int CallCount { get; private set; }

        public Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            throw exceptionToThrow;
        }
    }
}
