using System.Net;
using System.Net.Http.Json;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contract.Tests;

public sealed class OrchestrationEndpointContractTests : IClassFixture<ContractWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrchestrationEndpointContractTests(ContractWebApplicationFactory factory)
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

    private HttpClient CreateSupervisorClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantASupervisor.ApiKey);
        return client;
    }

    [Fact]
    public async Task OrchestrateEndpoint_EmptyQuery_ReturnsBadRequest()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrchestrateEndpoint_ValidQuery_ReturnsOkWithContractSchema()
    {
        var client = CreateOfficerClient();
        var response = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("How do I register a business?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OrchestrationApiResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.RunId);
        Assert.NotNull(result.CorrelationId);
        Assert.Equal("Sequential Pipeline with Human-in-the-Loop & Plain-RAG Fallback", result.PatternName);
        Assert.NotEmpty(result.AgentExecutions);
    }

    [Fact]
    public async Task OrchestrateEndpoint_QueryOverServerSideLimit_ReturnsBadRequest()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiSecurity:MaxQueryLength"] = "8"
                })));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantAOfficer.ApiKey);

        var response = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("123456789"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("QueryTooLong", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalEndpoints_FullLifecycle_EnforcesApprovalBoundary()
    {
        // Supervisor has all Officer permissions plus the SupervisorOnly decide/execute rights
        var client = CreateSupervisorClient();

        // Step 1: Run orchestration to generate a pending approval
        var orchResponse = await client.PostAsJsonAsync("/api/orchestrate", new OrchestrationApiRequest("How do I get a permit?"));
        Assert.Equal(HttpStatusCode.OK, orchResponse.StatusCode);
        var orchResult = await orchResponse.Content.ReadFromJsonAsync<OrchestrationApiResponse>();

        Assert.NotNull(orchResult?.PendingApproval);
        var requestId = orchResult.PendingApproval.RequestId;

        // Step 2: Attempting to execute consequential action while PENDING must fail with 400
        var executeBeforeApproval = await client.PostAsync($"/api/approvals/{requestId}/execute", null);
        Assert.Equal(HttpStatusCode.BadRequest, executeBeforeApproval.StatusCode);

        // Step 3: Fetch pending approval
        var getResponse = await client.GetAsync($"/api/approvals/{requestId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var pendingDto = await getResponse.Content.ReadFromJsonAsync<PendingApprovalDto>();
        Assert.NotNull(pendingDto);
        Assert.Equal("Pending", pendingDto.Decision);

        // Step 4: Submit Decision (Approve)
        var decideResponse = await client.PostAsJsonAsync(
            $"/api/approvals/{requestId}/decide",
            new ApprovalDecisionApiRequest("Approved", Comments: "Supervisor verified"));
        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);
        var decidedDto = await decideResponse.Content.ReadFromJsonAsync<PendingApprovalDto>();
        Assert.NotNull(decidedDto);
        Assert.Equal("Approved", decidedDto.Decision);

        // Step 5: Execute consequential action after approval must SUCCEED with 200
        var executeAfterApproval = await client.PostAsync($"/api/approvals/{requestId}/execute", null);
        Assert.Equal(HttpStatusCode.OK, executeAfterApproval.StatusCode);
        var execResult = await executeAfterApproval.Content.ReadFromJsonAsync<ApprovalExecutionApiResponse>();
        Assert.NotNull(execResult);
        Assert.True(execResult.Success);
        Assert.Equal("Approved", execResult.Status);
    }

    [Fact]
    public async Task GetApproval_NonExistentId_ReturnsNotFound()
    {
        var client = CreateOfficerClient();
        var response = await client.GetAsync("/api/approvals/non-existent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FakeChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId,
            float[] queryVector,
            int topK,
            CancellationToken cancellationToken)
        {
            var item = new VectorSearchResultItem(
                Guid.NewGuid(), Guid.NewGuid(), 0, "Permit Rules", "ref-101", "Requirements: valid ID and fee $50.", 0.10, 1);
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
                Guid.NewGuid(), Guid.NewGuid(), 0, "Permit Rules", "ref-101", "Requirements: valid ID and fee $50.", 0.90, 1);
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
        public string ProviderName => "Gemini";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChatCompletionResult(
                "You must present valid ID and pay $50. [1]",
                "Gemini",
                "gemini-2.5-flash",
                TimeSpan.FromMilliseconds(10)));
        }
    }
}
