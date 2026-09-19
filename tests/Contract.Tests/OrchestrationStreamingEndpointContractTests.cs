namespace Contract.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Streaming.Models;
using GovernmentDomainCopilot.Infrastructure.Auth;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class OrchestrationStreamingEndpointContractTests : IClassFixture<ContractWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrchestrationStreamingEndpointContractTests(ContractWebApplicationFactory factory)
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var efServices = services.Where(d =>
                    d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ServiceType.Namespace?.StartsWith("Npgsql") == true ||
                    (d.ImplementationType != null && d.ImplementationType.Namespace?.StartsWith("Npgsql") == true) ||
                    d.ServiceType.Name.Contains("DbContext")).ToList();

                foreach (var s in efServices)
                {
                    services.Remove(s);
                }

                services.AddDbContext<GovernmentDomainCopilotDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName)
                           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
                });

                var retrieverDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChunkRetriever));
                if (retrieverDescriptor != null) services.Remove(retrieverDescriptor);

                var keywordRetrieverDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKeywordChunkRetriever));
                if (keywordRetrieverDescriptor != null) services.Remove(keywordRetrieverDescriptor);

                var embeddingServiceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmbeddingService));
                if (embeddingServiceDescriptor != null) services.Remove(embeddingServiceDescriptor);

                var completionProviderDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatCompletionProvider));
                if (completionProviderDescriptor != null) services.Remove(completionProviderDescriptor);

                services.AddScoped<IChunkRetriever, FakeChunkRetriever>();
                services.AddScoped<IKeywordChunkRetriever, FakeKeywordChunkRetriever>();
                services.AddScoped<IEmbeddingService, FakeEmbeddingService>();
                services.AddScoped<IChatCompletionProvider, FakeChatCompletionProvider>();
            });
        });
    }

    private HttpClient CreateOfficerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantAOfficer.ApiKey);
        return client;
    }

    [Fact]
    public async Task Post_OrchestrateStream_EmptyQuery_Returns400BadRequest()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate/stream", new OrchestrationApiRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_OrchestrateStream_Returns200_WithTextEventStreamContentType()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate/stream", new OrchestrationApiRequest("How do I register a business?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task Post_OrchestrateStream_StreamsValidEvents_WithRunStarted_AndRunCompleted()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate/stream", new OrchestrationApiRequest("How do I register a business?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        Assert.NotEmpty(events);

        // Verify RunStarted is first event
        var first = events.First();
        Assert.Equal("RunStarted", first.EventType);

        var firstData = JsonSerializer.Deserialize<StreamProgressEvent>(first.Data, JsonOptions);
        Assert.NotNull(firstData);
        Assert.Equal(StreamEventType.RunStarted, firstData.EventType);

        // Verify terminal event is RunCompleted
        var terminal = events.Last();
        Assert.Equal("RunCompleted", terminal.EventType);

        var terminalData = JsonSerializer.Deserialize<StreamProgressEvent>(terminal.Data, JsonOptions);
        Assert.NotNull(terminalData);
        Assert.Equal(StreamEventType.RunCompleted, terminalData.EventType);
        Assert.NotNull(terminalData.FinalResponse);
        Assert.Equal(GroundedAnswerStatus.Grounded, terminalData.FinalResponse.Status);
    }

    [Fact]
    public async Task Post_OrchestrateStream_ClientTenantSpoofing_IsIgnored_AlwaysScopedToServerTenant()
    {
        var client = CreateOfficerClient();
        var spoofedTenantId = Guid.NewGuid();

        // Attempt client tenant spoofing via custom header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orchestrate/stream")
        {
            Content = JsonContent.Create(new OrchestrationApiRequest("Business registration requirements"))
        };
        request.Headers.Add("X-Tenant-ID", spoofedTenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);
        Assert.NotEmpty(events);

        // Invariant: Server tenant context is never overridden by client header
        foreach (var evt in events)
        {
            var parsed = JsonSerializer.Deserialize<StreamProgressEvent>(evt.Data, JsonOptions);
            Assert.NotNull(parsed);
            Assert.NotEqual(spoofedTenantId, parsed.TenantId);
        }
    }

    [Fact]
    public async Task Post_OrchestrateStream_TerminalEvent_IsAlwaysOneOfTerminalTypes()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate/stream", new OrchestrationApiRequest("What documents are required?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);
        Assert.NotEmpty(events);

        var terminal = events.Last();
        Assert.True(
            terminal.EventType is "RunCompleted" or "RunFailed" or "RunCancelled",
            $"Expected terminal event type, but got: {terminal.EventType}");
    }

    // --- SSE Parser Helper ---

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static List<(string EventType, string Data)> ParseSseEvents(string rawSse)
    {
        var list = new List<(string, string)>();
        var lines = rawSse.Split('\n');
        string? currentEvent = null;
        string? currentData = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim('\r');
            if (line.StartsWith("event: "))
            {
                currentEvent = line.Substring(7).Trim();
            }
            else if (line.StartsWith("data: "))
            {
                currentData = line.Substring(6).Trim();
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                if (currentEvent != null && currentData != null)
                {
                    list.Add((currentEvent, currentData));
                    currentEvent = null;
                    currentData = null;
                }
            }
        }

        if (currentEvent != null && currentData != null)
        {
            list.Add((currentEvent, currentData));
        }

        return list;
    }

    // --- Fakes ---

    private sealed class FakeChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId,
            float[] queryVector,
            int topK,
            CancellationToken cancellationToken)
        {
            var item = new VectorSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Business Guide", "ref-101", "Business registration requires Form A-1 and $100 fee. [1]", 0.10, 1);
            return Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(new[] { item });
        }
    }

    private sealed class FakeKeywordChunkRetriever : IKeywordChunkRetriever
    {
        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
            Guid tenantId,
            string query,
            int topK,
            CancellationToken cancellationToken)
        {
            var item = new KeywordSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Business Guide", "ref-101", "Business registration requires Form A-1 and $100 fee. [1]", 0.90, 1);
            return Task.FromResult<IReadOnlyList<KeywordSearchResultItem>>(new[] { item });
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            var vector = Enumerable.Repeat(0.1f, 768).ToList();
            var items = request.Inputs.Select((_, idx) => new EmbeddingItem(idx, vector)).ToList();
            return Task.FromResult(new EmbeddingResult("Stub", "stub-model", 768, items, TimeSpan.FromMilliseconds(5)));
        }
    }

    private sealed class FakeChatCompletionProvider : IChatCompletionProvider
    {
        public string ProviderName => "FakeGemini";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChatCompletionResult(
                "Business registration requires Form A-1 and $100 fee. [1]",
                ProviderName,
                "fake-model",
                TimeSpan.FromMilliseconds(50)));
        }

        public async IAsyncEnumerable<string> StreamCompleteAsync(
            ChatCompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "Business registration ";
            yield return "requires Form A-1 and $100 fee. ";
            yield return "[1]";
            await Task.CompletedTask;
        }
    }
}
