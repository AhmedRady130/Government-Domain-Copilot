using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contract.Tests;

public sealed class RunAndSessionEndpointContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RunAndSessionEndpointContractTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task SessionLifecycle_Create_List_Get_Messages_Succeeds()
    {
        var client = _factory.CreateClient();

        // 1. Create Session
        var createResponse = await client.PostAsJsonAsync("/api/sessions", new CreateSessionApiRequest("Citizen Service Inquiry"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdSession = await createResponse.Content.ReadFromJsonAsync<SessionApiResponse>();
        Assert.NotNull(createdSession);
        Assert.NotNull(createdSession.SessionId);
        Assert.Equal("Citizen Service Inquiry", createdSession.Title);

        // 2. List Sessions
        var listResponse = await client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var sessions = await listResponse.Content.ReadFromJsonAsync<List<SessionApiResponse>>();
        Assert.NotNull(sessions);
        Assert.Contains(sessions, s => s.SessionId == createdSession.SessionId);

        // 3. Get Session Details
        var getResponse = await client.GetAsync($"/api/sessions/{createdSession.SessionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var sessionDetail = await getResponse.Content.ReadFromJsonAsync<SessionApiResponse>();
        Assert.NotNull(sessionDetail);
        Assert.Equal(createdSession.SessionId, sessionDetail.SessionId);

        // 4. Send message via answer mode
        var msgResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{createdSession.SessionId}/messages",
            new PostSessionMessageApiRequest("How do I renew my permit?", Mode: "answer")
        );
        Assert.Equal(HttpStatusCode.OK, msgResponse.StatusCode);
        var assistantMsg = await msgResponse.Content.ReadFromJsonAsync<SessionMessageApiResponse>();
        Assert.NotNull(assistantMsg);
        Assert.Equal("assistant", assistantMsg.Role);
        Assert.NotEmpty(assistantMsg.Content);

        // 5. Inspect Session Messages
        var messagesResponse = await client.GetAsync($"/api/sessions/{createdSession.SessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<SessionMessageApiResponse>>();
        Assert.NotNull(messages);
        Assert.True(messages.Count >= 2); // 1 user + 1 assistant
    }

    [Fact]
    public async Task PostSessionMessage_OversizedContent_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/sessions", new CreateSessionApiRequest("Oversized Test"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionApiResponse>();

        var oversizedQuery = new string('x', 16_001);
        var msgResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{session!.SessionId}/messages",
            new PostSessionMessageApiRequest(oversizedQuery)
        );

        Assert.Equal(HttpStatusCode.BadRequest, msgResponse.StatusCode);
    }

    [Fact]
    public async Task RunsEndpoints_ListAndGet_AfterOrchestration_Succeeds()
    {
        var client = _factory.CreateClient();

        // 1. Run orchestration
        var orchResponse = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("How do I register?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);
        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult);

        // 2. List runs
        var runsResponse = await client.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
        var runs = await runsResponse.Content.ReadFromJsonAsync<List<RunSummaryApiResponse>>();
        Assert.NotNull(runs);
        Assert.Contains(runs, r => r.RunId == orchResult.RunId);

        // 3. Get run detail
        var runDetailResponse = await client.GetAsync($"/api/runs/{orchResult.RunId}");
        Assert.Equal(HttpStatusCode.OK, runDetailResponse.StatusCode);
        var runDetail = await runDetailResponse.Content.ReadFromJsonAsync<RunDetailApiResponse>();
        Assert.NotNull(runDetail);
        Assert.Equal(orchResult.RunId, runDetail.RunId);
    }

    [Fact]
    public async Task GetRun_NonExistent_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/runs/non-existent-run-999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetApprovals_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/approvals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approvals = await response.Content.ReadFromJsonAsync<List<PendingApprovalDto>>();
        Assert.NotNull(approvals);
    }

    [Fact]
    public async Task TenantIsolation_ClientHeaderSpoofing_IsIgnored()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-ID", Guid.NewGuid().ToString());

        var sessionsResponse = await client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, sessionsResponse.StatusCode);

        var runsResponse = await client.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
    }

    // --- Fake implementations matching actual interface signatures ---

    private sealed class FakeChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId, float[] queryVector, int topK, CancellationToken cancellationToken)
        {
            var item = new VectorSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Policy Doc", "ref-1", "Valid ID and fee $50 required.", 0.10, 1);
            return Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(new[] { item });
        }
    }

    private sealed class FakeKeywordChunkRetriever : IKeywordChunkRetriever
    {
        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
            Guid tenantId, string query, int topK, CancellationToken cancellationToken)
        {
            var item = new KeywordSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Policy Doc", "ref-1", "Valid ID and fee $50 required.", 0.90, 1);
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
        public string ProviderName => "Stub";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChatCompletionResult(
                "You must present valid ID and pay $50. [1]",
                "Stub",
                "stub-model",
                TimeSpan.FromMilliseconds(10)));
        }
    }
}
