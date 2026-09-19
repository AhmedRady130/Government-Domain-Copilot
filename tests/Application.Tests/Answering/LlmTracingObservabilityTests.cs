using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Exceptions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Answering.Services;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Models;
using GovernmentDomainCopilot.Application.Observability.Services;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.Tests.Answering;

/// <summary>
/// Tests proving FR-9 observability: LLM traces are recorded with the same
/// correlation ID that flows through the request. Uses InMemoryLlmTraceStore
/// (test-only store — never registered in production DI).
/// </summary>
public sealed class LlmTracingObservabilityTests
{
    private readonly Guid _tenantId = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffaaaabbbb");

    // ─── CORRELATION PROPAGATION ────────────────────────────────────────────

    [Fact]
    public async Task GetGroundedAnswerAsync_CorrelationId_PropagatesFromRequestToPersistedTrace()
    {
        // Arrange
        const string suppliedCorrelationId = "test-corr-id-12345";
        var traceStore = new InMemoryLlmTraceStore();
        var capturedRequests = new List<ChatCompletionRequest>();

        var sut = CreateSut(
            tenantId: _tenantId,
            traceStore: traceStore,
            onRequest: req => capturedRequests.Add(req));

        var request = new GroundedAnswerRequest(
            Query: "what is the tender deadline",
            CorrelationId: suppliedCorrelationId,
            RunId: "run-test-001");

        // Act
        await sut.GetGroundedAnswerAsync(request, CancellationToken.None);

        // Assert: same correlation ID on the completion request
        Assert.Single(capturedRequests);
        Assert.Equal(suppliedCorrelationId, capturedRequests[0].CorrelationId);

        // Assert: same correlation ID persisted in the LLM trace
        var traces = await traceStore.GetTracesByCorrelationIdAsync(suppliedCorrelationId, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        Assert.Equal(suppliedCorrelationId, traces[0].CorrelationId);
        Assert.Equal(_tenantId, traces[0].TenantId);
    }

    [Fact]
    public async Task GetGroundedAnswerAsync_NoCorrelationId_GeneratesAndPersistsCorrelationId()
    {
        // Arrange
        var traceStore = new InMemoryLlmTraceStore();
        var capturedRequests = new List<ChatCompletionRequest>();

        var sut = CreateSut(
            tenantId: _tenantId,
            traceStore: traceStore,
            onRequest: req => capturedRequests.Add(req));

        // Act: no CorrelationId supplied
        var request = new GroundedAnswerRequest("what is the registration process");
        await sut.GetGroundedAnswerAsync(request, CancellationToken.None);

        // Assert: a correlation ID was generated
        Assert.Single(capturedRequests);
        var generatedCorrelationId = capturedRequests[0].CorrelationId;
        Assert.False(string.IsNullOrWhiteSpace(generatedCorrelationId));
        Assert.StartsWith("corr-", generatedCorrelationId);

        // Assert: the generated correlation ID is persisted in the trace
        var traces = await traceStore.GetTracesByCorrelationIdAsync(generatedCorrelationId!, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        Assert.Equal(generatedCorrelationId, traces[0].CorrelationId);
    }

    [Fact]
    public async Task GetGroundedAnswerAsync_CorrelationContextSet_UsesContextCorrelationId()
    {
        // Arrange: ICorrelationContext (simulating what the HTTP middleware sets)
        const string middlewareCorrelationId = "middleware-corr-99999";
        var fakeCorrelationContext = new FakeCorrelationContext(middlewareCorrelationId);
        var traceStore = new InMemoryLlmTraceStore();
        var capturedRequests = new List<ChatCompletionRequest>();

        var sut = CreateSut(
            tenantId: _tenantId,
            traceStore: traceStore,
            correlationContext: fakeCorrelationContext,
            onRequest: req => capturedRequests.Add(req));

        // Act: no CorrelationId on request — should fall back to ICorrelationContext
        var request = new GroundedAnswerRequest("tender eligibility requirements");
        await sut.GetGroundedAnswerAsync(request, CancellationToken.None);

        // Assert: middleware-set correlation ID flows to completion request
        Assert.Single(capturedRequests);
        Assert.Equal(middlewareCorrelationId, capturedRequests[0].CorrelationId);

        // Assert: middleware correlation ID persisted in trace
        var traces = await traceStore.GetTracesByCorrelationIdAsync(middlewareCorrelationId, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        Assert.Equal(middlewareCorrelationId, traces[0].CorrelationId);
    }

    // ─── RUN ID PROPAGATION ──────────────────────────────────────────────────

    [Fact]
    public async Task GetGroundedAnswerAsync_RunId_PropagatesFromRequestToPersistedTrace()
    {
        // Arrange
        const string runId = "run-aaa-111";
        var traceStore = new InMemoryLlmTraceStore();

        var sut = CreateSut(tenantId: _tenantId, traceStore: traceStore);

        var request = new GroundedAnswerRequest(
            Query: "what documents are needed",
            RunId: runId);

        // Act
        await sut.GetGroundedAnswerAsync(request, CancellationToken.None);

        // Assert
        var traces = await traceStore.GetTracesByRunIdAsync(runId, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        Assert.Equal(runId, traces[0].RunId);
        Assert.Equal(_tenantId, traces[0].TenantId);
    }

    // ─── TENANT ISOLATION ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTracesByCorrelationId_DoesNotReturnOtherTenantTraces()
    {
        // Arrange
        const string correlationId = "shared-corr-id";
        var traceStore = new InMemoryLlmTraceStore();
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await traceStore.RecordTraceAsync(new LlmInvocationTrace(
            Id: Guid.NewGuid(),
            TenantId: tenantA,
            CorrelationId: correlationId,
            RunId: null,
            ProviderName: "Gemini",
            ModelName: "gemini-2.5-flash",
            OperationType: "ChatCompletion",
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(100),
            IsSuccess: true,
            PromptTokens: 100,
            CompletionTokens: 50,
            TotalTokens: 150,
            EstimatedCost: 0.0001m,
            ErrorMessage: null), CancellationToken.None);

        // Act: query as tenant B — must return empty
        var tenantBTraces = await traceStore.GetTracesByCorrelationIdAsync(correlationId, tenantB, CancellationToken.None);
        Assert.Empty(tenantBTraces);

        // Tenant A can see its own traces
        var tenantATraces = await traceStore.GetTracesByCorrelationIdAsync(correlationId, tenantA, CancellationToken.None);
        Assert.Single(tenantATraces);
    }

    // ─── TOKEN + COST ACCOUNTING ─────────────────────────────────────────────

    [Fact]
    public async Task GetGroundedAnswerAsync_WithUsageMetadata_RecordsTokensInTrace()
    {
        // Arrange: provider fires OnUsageResolved with token counts
        var traceStore = new InMemoryLlmTraceStore();
        var usageToReturn = new ChatCompletionUsageMetadata(
            PromptTokens: 200, CompletionTokens: 80, TotalTokens: 280);

        var sut = CreateSut(tenantId: _tenantId, traceStore: traceStore, usageMetadata: usageToReturn);

        const string corrId = "cost-test-corr";
        var request = new GroundedAnswerRequest("government fee schedule", CorrelationId: corrId);

        // Act
        await sut.GetGroundedAnswerAsync(request, CancellationToken.None);

        // Assert: token counts persisted
        var traces = await traceStore.GetTracesByCorrelationIdAsync(corrId, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        var trace = traces[0];
        Assert.Equal(200, trace.PromptTokens);
        Assert.Equal(80, trace.CompletionTokens);
        Assert.Equal(280, trace.TotalTokens);
        Assert.True(trace.IsSuccess);
    }

    // ─── FAILURE TRACES ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroundedAnswerAsync_ProviderThrows_RecordsOnlyStableFailureCode()
    {
        // Arrange
        var traceStore = new InMemoryLlmTraceStore();
        const string corrId = "failure-test-corr";

        var sut = CreateSut(tenantId: _tenantId, traceStore: traceStore, shouldThrow: true);

        const string sensitiveQuery = "government procedure for SSN 123-45-6789";
        var request = new GroundedAnswerRequest(sensitiveQuery, CorrelationId: corrId);

        // Act + Assert: exception re-thrown to caller
        await Assert.ThrowsAnyAsync<Exception>(() =>
            sut.GetGroundedAnswerAsync(request, CancellationToken.None));

        // Assert: failure trace was still persisted despite exception
        var traces = await traceStore.GetTracesByCorrelationIdAsync(corrId, _tenantId, CancellationToken.None);
        Assert.Single(traces);
        Assert.False(traces[0].IsSuccess);
        Assert.NotNull(traces[0].ErrorMessage);
        Assert.Equal("LlmInvocationFailed", traces[0].ErrorMessage);
        Assert.DoesNotContain(sensitiveQuery, traces[0].ErrorMessage!);
        Assert.DoesNotContain("api-key=SHOULD_BE_REDACTED", traces[0].ErrorMessage!);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private GroundedAnswerUseCase CreateSut(
        Guid tenantId,
        InMemoryLlmTraceStore? traceStore = null,
        ICorrelationContext? correlationContext = null,
        Action<ChatCompletionRequest>? onRequest = null,
        ChatCompletionUsageMetadata? usageMetadata = null,
        bool shouldThrow = false)
    {
        var item = new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), 0,
            "Official Doc", "gov.eg/doc1.pdf",
            "The tender fee is 500 EGP as per Decree 42.",
            0.95, 0.85, 0.01, 1, 0.90, 1);

        var search = new FakeHybridSearchUseCase(new[] { item });
        var provider = new FakeTracingCompletionProvider(onRequest, usageMetadata, shouldThrow);
        var costCalc = new ModelPricingCostCalculator(
            Options.Create(new ModelPricingOptions()));

        return new GroundedAnswerUseCase(
            new FakeTenantContext(tenantId),
            search,
            provider,
            new EvidenceSufficiencyPolicy(),
            new CitationValidator(),
            NullLogger<GroundedAnswerUseCase>.Instance,
            correlationContext,
            traceStore,
            costCalc);
    }

    // ─── FAKES ───────────────────────────────────────────────────────────────

    private sealed class FakeTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;
        public FakeTenantContext(Guid tenantId) => _tenantId = tenantId;
        public Guid GetTenantId() => _tenantId;
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        private string _id;
        public FakeCorrelationContext(string id) => _id = id;
        public string CorrelationId => _id;
        public void SetCorrelationId(string id) => _id = id;
    }

    private sealed class FakeHybridSearchUseCase : IHybridSearchUseCase
    {
        private readonly IReadOnlyList<RerankResultItem> _items;
        public FakeHybridSearchUseCase(IReadOnlyList<RerankResultItem>? items = null)
            => _items = items ?? Array.Empty<RerankResultItem>();

        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new HybridSearchResponse(
                request.TopK ?? 5, _items.Count, TimeSpan.FromMilliseconds(5),
                "HybridRRF", "pgvector+tsvector", _items));
    }

    private sealed class FakeTracingCompletionProvider : IChatCompletionProvider
    {
        private readonly Action<ChatCompletionRequest>? _onRequest;
        private readonly ChatCompletionUsageMetadata? _usage;
        private readonly bool _shouldThrow;
        public string ProviderName => "Gemini";

        public FakeTracingCompletionProvider(
            Action<ChatCompletionRequest>? onRequest,
            ChatCompletionUsageMetadata? usage,
            bool shouldThrow)
        {
            _onRequest = onRequest;
            _usage = usage;
            _shouldThrow = shouldThrow;
        }

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            _onRequest?.Invoke(request);
            if (_shouldThrow)
                throw new LlmProviderUnavailableException(
                    "Gemini",
                    $"Simulated failure for {request.UserPrompt}. api-key=SHOULD_BE_REDACTED");

            if (_usage != null)
                request.OnUsageResolved?.Invoke(_usage);

            return Task.FromResult(new ChatCompletionResult(
                "The tender deadline is Monday [1].",
                "Gemini", "gemini-2.5-flash",
                TimeSpan.FromMilliseconds(10),
                _usage));
        }

        public async IAsyncEnumerable<string> StreamCompleteAsync(
            ChatCompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _onRequest?.Invoke(request);
            yield return "The tender deadline is Monday [1].";
            if (_usage != null)
                request.OnUsageResolved?.Invoke(_usage);
            await Task.CompletedTask;
        }
    }
}
