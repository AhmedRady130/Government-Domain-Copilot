using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contract.Tests;

public sealed class IngestDocumentEndpointContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestDocumentEndpointContractTests(WebApplicationFactory<Program> factory)
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
            });
        });
    }

    [Fact]
    public async Task Ingest_valid_document_returns_201_Created_and_typed_response()
    {
        var client = _factory.CreateClient();
        var payload = new IngestDocumentApiRequest("Executive Decree 404", "gov-ref-404", "First line of Decree.\nSecond line of Decree.");

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<IngestDocumentApiResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.DocumentId);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task Ingest_missing_title_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new IngestDocumentApiRequest(string.Empty, "gov-ref-empty-title", "Some valid source text.");

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title", json);
    }

    [Fact]
    public async Task Ingest_missing_source_text_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new IngestDocumentApiRequest("Valid Title", "gov-ref-empty-text", "   ");

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceText", json);
    }

    [Fact]
    public async Task Ingest_oversized_title_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var oversizedTitle = new string('T', 501);
        var payload = new IngestDocumentApiRequest(oversizedTitle, "gov-ref-oversized-title", "Valid source text.");

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_oversized_source_reference_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var oversizedRef = new string('R', 2001);
        var payload = new IngestDocumentApiRequest("Valid Title", oversizedRef, "Valid source text.");

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_oversized_source_text_returns_400_BadRequest()
    {
        var client = _factory.CreateClient();
        var oversizedText = new string('A', 500_001);
        var payload = new IngestDocumentApiRequest("Valid Title", "gov-ref-oversized-text", oversizedText);

        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_payload_attempting_to_supply_TenantId_is_ignored_by_api()
    {
        var client = _factory.CreateClient();
        var customTenantId = Guid.NewGuid();

        var jsonPayload = JsonSerializer.Serialize(new
        {
            Title = "Payload Injection Attempt",
            SourceReference = "gov-ref-injection",
            SourceText = "Text content for injection test.",
            TenantId = customTenantId
        });

        var content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<IngestDocumentApiResponse>();
        Assert.NotNull(result);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var docWithClientTenant = await repo.GetByIdAsync(customTenantId, result.DocumentId, CancellationToken.None);

        Assert.Null(docWithClientTenant);
    }

    [Fact]
    public async Task Server_tenant_context_header_determines_persisted_tenant_identity()
    {
        var client = _factory.CreateClient();
        var expectedHeaderTenantId = Guid.NewGuid();

        client.DefaultRequestHeaders.Add("X-Tenant-ID", expectedHeaderTenantId.ToString());

        var payload = new IngestDocumentApiRequest("Header Tenant Doc", "gov-ref-header-tenant", "Content for header tenant doc.");
        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestDocumentApiResponse>();
        Assert.NotNull(result);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var docWithHeaderTenant = await repo.GetByIdAsync(expectedHeaderTenantId, result.DocumentId, CancellationToken.None);
        Assert.NotNull(docWithHeaderTenant);
        Assert.Equal(expectedHeaderTenantId, docWithHeaderTenant.TenantId);
    }

    [Fact]
    public async Task API_does_not_leak_internal_exception_details()
    {
        var client = _factory.CreateClient();

        var content = new System.Net.Http.StringContent("{ invalid json }", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("Npgsql", json);
        Assert.DoesNotContain("at GovernmentDomainCopilot", json);
    }

    [Fact]
    public async Task Ingestion_produces_expected_document_and_chunk_count_in_persistence()
    {
        var client = _factory.CreateClient();
        var tenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantId.ToString());

        var payload = new IngestDocumentApiRequest("Multi-chunk Decree", "gov-ref-multi-chunk", "Chunk 1 text.\n\nChunk 2 text.\n\nChunk 3 text.");
        var response = await client.PostAsJsonAsync("/api/documents", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestDocumentApiResponse>();
        Assert.NotNull(result);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var doc = await repo.GetByIdAsync(tenantId, result.DocumentId, CancellationToken.None);
        var chunks = await repo.GetChunksByDocumentIdAsync(tenantId, result.DocumentId, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("Multi-chunk Decree", doc.Title);
        Assert.Equal(result.ChunkCount, chunks.Count);
    }
}
