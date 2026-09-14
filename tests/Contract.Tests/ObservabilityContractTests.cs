using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.API.Middleware;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Services;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Infrastructure.Auth;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace Contract.Tests;

/// <summary>
/// Contract tests for FR-9: correlation ID propagation through the HTTP layer
/// and LLM trace endpoint access control.
/// </summary>
public sealed class ObservabilityContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    // Shared InMemoryLlmTraceStore so we can inspect persisted traces from tests
    private readonly InMemoryLlmTraceStore _traceStore = new();

    public ObservabilityContractTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace EF Core with InMemory for tests
                var efServices = services.Where(d =>
                    d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ServiceType.Namespace?.StartsWith("Npgsql") == true ||
                    (d.ImplementationType != null && d.ImplementationType.Namespace?.StartsWith("Npgsql") == true) ||
                    d.ServiceType.Name.Contains("DbContext")).ToList();
                foreach (var s in efServices) services.Remove(s);

                services.AddDbContext<GovernmentDomainCopilotDbContext>(options =>
                    options.UseInMemoryDatabase(dbName)
                           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

                // Replace external providers with fakes
                RemoveAndReplace<IChunkRetriever>(services, _ => services.AddScoped<IChunkRetriever, FakeChunkRetriever>());
                RemoveAndReplace<IKeywordChunkRetriever>(services, _ => services.AddScoped<IKeywordChunkRetriever, FakeKeywordChunkRetriever>());
                RemoveAndReplace<IEmbeddingService>(services, _ => services.AddScoped<IEmbeddingService, FakeEmbeddingService>());
                RemoveAndReplace<IChatCompletionProvider>(services, _ => services.AddScoped<IChatCompletionProvider, FakeChatCompletionProvider>());

                // Replace PostgresLlmTraceStore with the shared in-memory store
                var llmTraceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILlmTraceStore));
                if (llmTraceDescriptor != null) services.Remove(llmTraceDescriptor);
                services.AddSingleton<ILlmTraceStore>(_traceStore);
            });
        });
    }

    private static void RemoveAndReplace<T>(IServiceCollection services, Action<IServiceCollection> add)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
        add(services);
    }

    // ─── CORRELATION HEADER PROPAGATION ─────────────────────────────────────

    [Fact]
    public async Task Request_WithCorrelationHeader_EchoesCorrelationIdInResponse()
    {
        // Arrange
        var client = CreateOfficerClient();
        const string correlationId = "test-corr-header-echo-001";
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.RequestHeaderName, correlationId);

        // Act: any authenticated endpoint; health is anonymous
        var response = await client.GetAsync("/health");

        // Assert: X-Correlation-ID echoed in response
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.ResponseHeaderName),
            "Response must contain X-Correlation-ID header.");
        var echoedId = response.Headers.GetValues(CorrelationIdMiddleware.ResponseHeaderName).First();
        Assert.Equal(correlationId, echoedId);
    }

    [Fact]
    public async Task Request_WithoutCorrelationHeader_GeneratesCorrelationIdInResponse()
    {
        // Arrange
        var client = CreateOfficerClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert: a correlation ID was generated and set in the response
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.ResponseHeaderName),
            "Response must always contain X-Correlation-ID header (generated when not supplied).");
        var echoedId = response.Headers.GetValues(CorrelationIdMiddleware.ResponseHeaderName).First();
        Assert.False(string.IsNullOrWhiteSpace(echoedId));
        Assert.StartsWith("corr-", echoedId);
    }

    // ─── TRACE ENDPOINT — ACCESS CONTROL ────────────────────────────────────

    [Fact]
    public async Task GetLlmTraces_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/traces/llm?correlationId=xyz");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLlmTraces_NoQueryParam_Returns400()
    {
        var client = CreateOfficerClient();
        var response = await client.GetAsync("/api/traces/llm");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLlmTraces_ValidCorrelationId_Returns200WithEmptyList()
    {
        // Arrange: a correlation ID that has no traces yet
        var client = CreateOfficerClient();

        // Act
        var response = await client.GetAsync("/api/traces/llm?correlationId=no-traces-yet");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<LlmTraceApiResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task GetLlmTraces_ByRunId_Returns200WithEmptyList()
    {
        // Arrange
        var client = CreateOfficerClient();

        // Act
        var response = await client.GetAsync("/api/traces/llm?runId=no-run-yet");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<LlmTraceApiResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    // ─── HEALTH & READINESS ENDPOINTS ───────────────────────────────────────

    [Fact]
    public async Task Health_IsAnonymous_AndReturns200Healthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task HealthLive_IsAnonymous_AndReturns200Healthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task Ready_WhenDbConnects_IsAnonymous_AndReturns200Ready()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ready", content);
    }

    [Fact]
    public async Task Ready_WhenDbFails_Returns503ServiceUnavailable_AndSafeResponse()
    {
        // Create a client from a factory whose DbContext cannot connect
        var brokenFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var efServices = services.Where(d =>
                    d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ServiceType.Namespace?.StartsWith("Npgsql") == true ||
                    (d.ImplementationType != null && d.ImplementationType.Namespace?.StartsWith("Npgsql") == true) ||
                    d.ServiceType.Name.Contains("DbContext")).ToList();
                foreach (var s in efServices) services.Remove(s);

                // Configure with an invalid connection string pointing to unreachable port
                services.AddDbContext<GovernmentDomainCopilotDbContext>(options =>
                {
                    options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1;CommandTimeout=1");
                });
            });
        });

        var client = brokenFactory.CreateClient();
        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("NotReady", content);

        // Verify no sensitive connection or exception details leak
        Assert.DoesNotContain("Host=127.0.0.1", content);
        Assert.DoesNotContain("unreachable", content);
        Assert.DoesNotContain("StackTrace", content);
        Assert.DoesNotContain("Npgsql", content);
        Assert.DoesNotContain("Exception", content);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private HttpClient CreateOfficerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantAOfficer.ApiKey);
        return client;
    }

    // ─── FAKES ───────────────────────────────────────────────────────────────

    private sealed class FakeChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId, float[] queryVector, int topK, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(Array.Empty<VectorSearchResultItem>());
    }

    private sealed class FakeKeywordChunkRetriever : IKeywordChunkRetriever
    {
        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
            Guid tenantId, string query, int topK, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<KeywordSearchResultItem>>(Array.Empty<KeywordSearchResultItem>());
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new EmbeddingResult(
                "Gemini", "text-embedding-004", 768,
                Array.Empty<EmbeddingItem>(), TimeSpan.Zero));
    }

    private sealed class FakeChatCompletionProvider : IChatCompletionProvider
    {
        public string ProviderName => "Gemini";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChatCompletionResult(
                "Answer text [1].", "Gemini", "gemini-2.5-flash", TimeSpan.FromMilliseconds(5)));

        public async IAsyncEnumerable<string> StreamCompleteAsync(
            ChatCompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return "Answer text [1].";
            await Task.CompletedTask;
        }
    }
}
