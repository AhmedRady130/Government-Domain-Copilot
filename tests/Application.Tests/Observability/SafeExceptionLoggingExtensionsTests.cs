using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Observability;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging;

namespace Application.Tests.Observability;

public sealed class SafeExceptionLoggingExtensionsTests
{
    [Fact]
    public void LogSafeFailure_ExcludesExceptionMessageAndIncludesSafeDiagnosticFields()
    {
        var logger = new CapturingLogger();

        logger.LogSafeFailure(
            new InvalidOperationException("FAKE-SENSITIVE-QUERY-123 FAKE-CREDENTIAL-456"),
            "TestFailure", "TestOperation", "corr-test-789", TimeSpan.FromMilliseconds(12));

        var output = Assert.Single(logger.Messages);
        Assert.Contains("TestFailure", output);
        Assert.Contains("corr-test-789", output);
        Assert.Contains(nameof(InvalidOperationException), output);
        Assert.DoesNotContain("FAKE-SENSITIVE-QUERY-123", output);
        Assert.DoesNotContain("FAKE-CREDENTIAL-456", output);
    }

    [Fact]
    public async Task VectorSearchFailure_LogsSafeFailureMetadataWithoutExceptionText()
    {
        var logger = new CapturingLogger<VectorSearchUseCase>();
        var sut = new VectorSearchUseCase(
            new TenantContext(Guid.NewGuid()),
            new ThrowingEmbeddingService(),
            new NoOpChunkRetriever(),
            logger);

        await Assert.ThrowsAsync<VectorSearchException>(() =>
            sut.SearchAsync(new VectorSearchRequest("safe query"), CancellationToken.None));

        var output = Assert.Single(logger.Messages);
        Assert.Contains("VectorEmbeddingGenerationFailed", output);
        Assert.Contains(nameof(InvalidOperationException), output);
        Assert.DoesNotContain("FAKE-SENSITIVE-QUERY-123", output);
        Assert.DoesNotContain("FAKE-CREDENTIAL-456", output);
    }

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid GetTenantId() => tenantId;
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("FAKE-SENSITIVE-QUERY-123 FAKE-CREDENTIAL-456");
    }

    private sealed class NoOpChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId, float[] queryVector, int topK, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(Array.Empty<VectorSearchResultItem>());
    }

    private class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class CapturingLogger<T> : CapturingLogger, ILogger<T> { }
}
