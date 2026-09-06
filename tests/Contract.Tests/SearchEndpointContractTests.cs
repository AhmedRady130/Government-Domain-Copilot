using System.Net;
using System.Net.Http.Json;
using GovernmentDomainCopilot.API.Models;
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

public sealed class SearchEndpointContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SearchEndpointContractTests(WebApplicationFactory<Program> factory)
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
                if (retrieverDescriptor != null)
                {
                    services.Remove(retrieverDescriptor);
                }

                var keywordRetrieverDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKeywordChunkRetriever));
                if (keywordRetrieverDescriptor != null)
                {
                    services.Remove(keywordRetrieverDescriptor);
                }

                var embeddingServiceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmbeddingService));
                if (embeddingServiceDescriptor != null)
                {
                    services.Remove(embeddingServiceDescriptor);
                }

                services.AddScoped<IChunkRetriever, StubChunkRetriever>();
                services.AddScoped<IKeywordChunkRetriever, StubKeywordChunkRetriever>();
                services.AddScoped<IEmbeddingService, StubEmbeddingService>();
            });
        });
    }

    [Fact]
    public async Task Search_empty_query_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/search?query=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Validation failed", json);
    }

    [Fact]
    public async Task Search_valid_query_returns_200_OK_and_SearchApiResponse()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/search?query=procurement%20decree&topK=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SearchApiResponse>();
        Assert.NotNull(result);
        Assert.Equal(3, result.TopK);
        Assert.NotNull(result.Items);
    }

    [Fact]
    public async Task Search_does_not_leak_internal_exception_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/search?query=valid");

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("Npgsql", json);
        Assert.DoesNotContain("at GovernmentDomainCopilot", json);
    }

    private sealed class StubChunkRetriever : IChunkRetriever
    {
        public Task<IReadOnlyList<VectorSearchResultItem>> SearchVectorAsync(
            Guid tenantId,
            float[] queryVector,
            int topK,
            CancellationToken cancellationToken)
        {
            var dummyItem = new VectorSearchResultItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                "Sample Title",
                "sample-ref",
                "Sample chunk content.",
                distance: 0.15,
                rank: 1);

            return Task.FromResult<IReadOnlyList<VectorSearchResultItem>>(new[] { dummyItem });
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
            var dummyItem = new KeywordSearchResultItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                "Sample Title",
                "sample-ref",
                "Sample chunk content.",
                KeywordScore: 0.85,
                Rank: 1);

            return Task.FromResult<IReadOnlyList<KeywordSearchResultItem>>(new[] { dummyItem });
        }
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            var vector = Enumerable.Repeat(0.1f, 768).ToList();
            var items = request.Inputs.Select((_, idx) => new EmbeddingItem(idx, vector)).ToList();
            return Task.FromResult(new EmbeddingResult("StubProvider", "stub-model", 768, items, TimeSpan.FromMilliseconds(5)));
        }
    }
}
