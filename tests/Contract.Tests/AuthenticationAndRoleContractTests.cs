using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Embeddings.Abstractions;
using GovernmentDomainCopilot.Application.Embeddings.Models;
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
/// FR-8 contract tests: authentication enforcement, role-based access control,
/// and claims-based tenant identity isolation.
/// </summary>
public sealed class AuthenticationAndRoleContractTests : IClassFixture<ContractWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationAndRoleContractTests(ContractWebApplicationFactory factory)
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
                    services.Remove(s);

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

                services.AddScoped<IChunkRetriever, StubChunkRetriever>();
                services.AddScoped<IKeywordChunkRetriever, StubKeywordChunkRetriever>();
                services.AddScoped<IEmbeddingService, StubEmbeddingService>();
                services.AddSingleton<IChatCompletionProvider, StubChatCompletionProvider>();
            });
        });
    }

    private HttpClient CreateUnauthenticatedClient() => _factory.CreateClient();

    private HttpClient CreateOfficerClient(string? apiKey = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName,
            apiKey ?? SeedAuthIdentities.TenantAOfficer.ApiKey);
        return client;
    }

    private HttpClient CreateSupervisorClient(string? apiKey = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName,
            apiKey ?? SeedAuthIdentities.TenantASupervisor.ApiKey);
        return client;
    }

    // ── 401 Unauthenticated Tests ─────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_PostAnswer_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/answer", new GroundedAnswerApiRequest("Test query"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_GetSessions_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_PostOrchestrate_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("Test query"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_GetRuns_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidApiKey_PostAnswer_Returns401()
    {
        var client = CreateOfficerClient("invalid-key-that-does-not-exist");
        var response = await client.PostAsJsonAsync("/api/answer", new GroundedAnswerApiRequest("Test query"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 Role Enforcement Tests ────────────────────────────────────────────

    [Fact]
    public async Task Officer_PostDecide_Returns403Forbidden()
    {
        var client = CreateOfficerClient();

        var orchResponse = await client.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I get a permit?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);

        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        var decideResponse = await client.PostAsJsonAsync(
            $"/api/approvals/{requestId}/decide",
            new ApprovalDecisionApiRequest("Approved", Comments: "Officer should not decide"));

        Assert.Equal(HttpStatusCode.Forbidden, decideResponse.StatusCode);
    }

    [Fact]
    public async Task Supervisor_PostDecide_Returns200Ok()
    {
        var client = CreateSupervisorClient();

        var orchResponse = await client.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I register?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);

        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        var decideResponse = await client.PostAsJsonAsync(
            $"/api/approvals/{requestId}/decide",
            new ApprovalDecisionApiRequest("Approved", Comments: "Supervisor approved"));

        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);
    }

    // ── Cross-Tenant Isolation Tests ──────────────────────────────────────────

    [Fact]
    public async Task TenantA_Cannot_See_TenantB_Sessions()
    {
        var tenantBClient = CreateOfficerClient(SeedAuthIdentities.TenantBOfficer.ApiKey);

        var createResponse = await tenantBClient.PostAsJsonAsync("/api/sessions",
            new CreateSessionApiRequest("Tenant B Private Session"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var tenantBSession = await createResponse.Content.ReadFromJsonAsync<SessionApiResponse>();
        Assert.NotNull(tenantBSession);

        var tenantAClient = CreateOfficerClient(SeedAuthIdentities.TenantAOfficer.ApiKey);
        var listResponse = await tenantAClient.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var sessions = await listResponse.Content.ReadFromJsonAsync<List<SessionApiResponse>>();
        Assert.NotNull(sessions);
        Assert.DoesNotContain(sessions, s => s.SessionId == tenantBSession.SessionId);
    }

    [Fact]
    public async Task TenantA_Cannot_See_TenantB_Runs()
    {
        var tenantBClient = CreateOfficerClient(SeedAuthIdentities.TenantBOfficer.ApiKey);

        var orchResponse = await tenantBClient.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I register?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);
        var tenantBRun = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(tenantBRun);

        var tenantAClient = CreateOfficerClient(SeedAuthIdentities.TenantAOfficer.ApiKey);
        var runsResponse = await tenantAClient.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);

        var runs = await runsResponse.Content.ReadFromJsonAsync<List<RunSummaryApiResponse>>();
        Assert.NotNull(runs);
        Assert.DoesNotContain(runs, r => r.RunId == tenantBRun.RunId);
    }

    [Fact]
    public async Task Claims_Based_TenantId_Is_Used_Not_Spoofed_Header()
    {
        var client = CreateOfficerClient(SeedAuthIdentities.TenantAOfficer.ApiKey);
        // Spoof a different tenant ID via custom header — must be ignored by the server
        client.DefaultRequestHeaders.Add("X-Tenant-ID", SeedAuthIdentities.TenantBId.ToString());

        var response = await client.GetAsync("/api/sessions");
        // Request must succeed (auth is from API key / claim, not the spoofed header)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_IsAnonymous_Returns200Ok()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Officer_PostExecute_Returns403Forbidden()
    {
        var client = CreateOfficerClient();

        var orchResponse = await client.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I get a permit?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);

        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        var executeResponse = await client.PostAsync($"/api/approvals/{requestId}/execute", null);
        Assert.Equal(HttpStatusCode.Forbidden, executeResponse.StatusCode);
    }

    [Fact]
    public async Task Supervisor_DecideAndExecute_Succeeds()
    {
        var officerClient = CreateOfficerClient();
        var supervisorClient = CreateSupervisorClient();

        // 1. Officer triggers orchestration that produces an approval request
        var orchResponse = await officerClient.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I register a business?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);

        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        // 2. Supervisor approves the request
        var decideResponse = await supervisorClient.PostAsJsonAsync(
            $"/api/approvals/{requestId}/decide",
            new ApprovalDecisionApiRequest("Approved", Comments: "Approved by supervisor"));
        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);

        // 3. Supervisor executes the approved request
        var executeResponse = await supervisorClient.PostAsync($"/api/approvals/{requestId}/execute", null);
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
    }

    [Fact]
    public async Task TenantA_Supervisor_Cannot_Access_TenantB_Approval()
    {
        var tenantBOfficerClient = CreateOfficerClient(SeedAuthIdentities.TenantBOfficer.ApiKey);
        var tenantASupervisorClient = CreateSupervisorClient(SeedAuthIdentities.TenantASupervisor.ApiKey);

        // Tenant B officer creates approval request
        var orchResponse = await tenantBOfficerClient.PostAsJsonAsync("/api/orchestrate",
            new OrchestrationApiRequest("How do I get a permit?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);
        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        // Tenant A supervisor tries to get Tenant B's approval
        var getResponse = await tenantASupervisorClient.GetAsync($"/api/approvals/{requestId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // Tenant A supervisor tries to decide Tenant B's approval
        var decideResponse = await tenantASupervisorClient.PostAsJsonAsync(
            $"/api/approvals/{requestId}/decide",
            new ApprovalDecisionApiRequest("Approved", Comments: "Unauthorized cross-tenant attempt"));
        Assert.Equal(HttpStatusCode.NotFound, decideResponse.StatusCode);
    }

    [Fact]
    public async Task AuthorizationHeader_BearerScheme_IsAccepted()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SeedAuthIdentities.TenantAOfficer.ApiKey}");

        var response = await client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationHeader_ApiKeyScheme_IsAccepted()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {SeedAuthIdentities.TenantAOfficer.ApiKey}");

        var response = await client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_PostDocument_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/documents", new IngestDocumentApiRequest("Title", "Ref", "Content"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_GetSearch_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/search?query=test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SeedApiKey_InProductionEnvironment_IsRejected_Returns401()
    {
        using var prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
        });

        var client = prodFactory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantAOfficer.ApiKey);

        var response = await client.PostAsJsonAsync("/api/answer", new GroundedAnswerApiRequest("Test query"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId, float[] queryVector, int topK, CancellationToken cancellationToken)
        {
            var item = new VectorSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Permit Rules", "ref-101",
                "Requirements: valid ID and fee $50.", 0.10, 1);
            return Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(new[] { item });
        }
    }

    private sealed class StubKeywordChunkRetriever : IKeywordChunkRetriever
    {
        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
            Guid tenantId, string query, int topK, CancellationToken cancellationToken)
        {
            var item = new KeywordSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Permit Rules", "ref-101",
                "Requirements: valid ID and fee $50.", 0.90, 1);
            return Task.FromResult<IReadOnlyList<KeywordSearchResultItem>>(new[] { item });
        }
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            var vector = Enumerable.Repeat(0.1f, 768).ToList();
            var items = request.Inputs.Select((_, idx) => new EmbeddingItem(idx, vector)).ToList();
            return Task.FromResult(new EmbeddingResult("Stub", "stub-model", 768, items, TimeSpan.FromMilliseconds(5)));
        }
    }

    private sealed class StubChatCompletionProvider : IChatCompletionProvider
    {
        public string ProviderName => "Stub";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChatCompletionResult(
                "You must present valid ID and pay $50. [1]",
                "Stub", "stub-model", TimeSpan.FromMilliseconds(10)));
        }
    }
}
