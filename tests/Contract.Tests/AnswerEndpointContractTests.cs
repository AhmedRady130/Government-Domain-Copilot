using System.Net;
using System.Net.Http.Json;
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

public sealed class AnswerEndpointContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TestChunkId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestDocId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly WebApplicationFactory<Program> _factory;

    public AnswerEndpointContractTests(WebApplicationFactory<Program> factory)
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

                services.AddScoped<IChunkRetriever, StubChunkRetriever>();
                services.AddScoped<IKeywordChunkRetriever, StubKeywordChunkRetriever>();
                services.AddScoped<IEmbeddingService, StubEmbeddingService>();
                services.AddSingleton<IChatCompletionProvider, StubChatCompletionProvider>();
            });
        });
    }

    /// <summary>
    /// Creates an authenticated HTTP client with the Officer API key.
    /// All existing contract test scenarios test behaviour under valid authenticated identity.
    /// </summary>
    private HttpClient CreateOfficerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, SeedAuthIdentities.TenantAOfficer.ApiKey);
        return client;
    }

    [Fact]
    public async Task PostAnswer_ValidQuery_Returns200OKWithGroundedStatusAndCitations()
    {
        var client = CreateOfficerClient();
        var request = new GroundedAnswerApiRequest("What is the tender filing fee?");

        var response = await client.PostAsJsonAsync("/api/answer", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<GroundedAnswerApiResponse>();
        Assert.NotNull(result);
        Assert.Equal("Grounded", result.Status);
        Assert.NotNull(result.Answer);
        Assert.NotEmpty(result.Citations);
        Assert.Equal("[1]", result.Citations[0].CitationId);
        Assert.Equal(TestChunkId, result.Citations[0].ChunkId);
    }

    [Fact]
    public async Task PostAnswer_EmptyQuery_Returns400BadRequest()
    {
        var client = CreateOfficerClient();
        var request = new GroundedAnswerApiRequest("");

        var response = await client.PostAsJsonAsync("/api/answer", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAnswer_CallerCannotSupplyTenantId()
    {
        var client = CreateOfficerClient();
        // Post payload with unknown JSON property "tenantId"
        var jsonPayload = new { query = "tender filing fee", tenantId = Guid.NewGuid().ToString() };

        var response = await client.PostAsJsonAsync("/api/answer", jsonPayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // System must ignore client-supplied tenantId and use ITenantContext
        var result = await response.Content.ReadFromJsonAsync<GroundedAnswerApiResponse>();
        Assert.NotNull(result);
        Assert.Equal("Grounded", result.Status);
    }

    [Fact]
    public async Task PostAnswer_CallerCannotSwitchTenantViaHeader()
    {
        var client = CreateOfficerClient();
        var spoofedTenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-ID", spoofedTenantId.ToString());

        var request = new GroundedAnswerApiRequest("tender filing fee");
        var response = await client.PostAsJsonAsync("/api/answer", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Request still succeeds with the authoritative configured development tenant
        var result = await response.Content.ReadFromJsonAsync<GroundedAnswerApiResponse>();
        Assert.NotNull(result);
        Assert.Equal("Grounded", result.Status);
    }

    [Fact]
    public async Task PostAnswer_DoesNotLeakInternalProviderErrorsOrSecrets()
    {
        var client = CreateOfficerClient();
        var request = new GroundedAnswerApiRequest("valid query");

        var response = await client.PostAsJsonAsync("/api/answer", request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("GEMINI_API_KEY", json);
        Assert.DoesNotContain("x-goog-api-key", json);
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("at GovernmentDomainCopilot", json);
    }

    [Fact]
    public async Task PostAnswer_ResponseContainsCitationsOnlyForRetrievedEvidence()
    {
        var client = CreateOfficerClient();
        var request = new GroundedAnswerApiRequest("tender filing fee");

        var response = await client.PostAsJsonAsync("/api/answer", request);
        var result = await response.Content.ReadFromJsonAsync<GroundedAnswerApiResponse>();

        Assert.NotNull(result);
        Assert.All(result.Citations, c => Assert.Equal(TestChunkId, c.ChunkId));
    }

    private sealed class StubChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId,
            float[] queryVector,
            int topK,
            CancellationToken cancellationToken)
        {
            var item = new VectorSearchResultItem(
                TestChunkId, TestDocId, 0, "Tender Policy", "ref-101", "Filing fee for tender is 500 EGP.", 0.10, 1);

            return Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(new[] { item });
        }
    }

    private sealed class StubKeywordChunkRetriever : IKeywordChunkRetriever
    {
        public Task<IReadOnlyList<KeywordSearchResultItem>> SearchKeywordAsync(
            Guid tenantId,
            string query,
            int topK,
            CancellationToken cancellationToken)
        {
            var item = new KeywordSearchResultItem(
                TestChunkId, TestDocId, 0, "Tender Policy", "ref-101", "Filing fee for tender is 500 EGP.", 0.90, 1);

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
        public string ProviderName => "Gemini";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChatCompletionResult(
                "Filing fee for tender is 500 EGP [1].",
                "Gemini",
                "gemini-2.5-flash",
                TimeSpan.FromMilliseconds(10)));
        }
    }
}
