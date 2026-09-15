using System.Net;
using System.Text;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Answering.Services;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.LLM.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Tests.Answering;

/// <summary>Proves configuration swaps only the Infrastructure adapter, not the grounded-answer workflow.</summary>
public sealed class ChatProviderSwapAcceptanceTests
{
    [Theory]
    [InlineData(OllamaChatCompletionProvider.Name, "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"The filing fee is 5 demo credits [1].\"},\"done\":true}", "Ollama")]
    [InlineData(GeminiChatCompletionProvider.Name, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"The filing fee is 5 demo credits [1].\"}]}}]}", "Gemini")]
    public async Task ConfigurationSwapsProviderWhileGroundedAnswerWorkflowRemainsUnchanged(
        string configuredProvider,
        string responseJson,
        string expectedProvider)
    {
        var handler = new StaticResponseHandler(responseJson);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(CreateConfiguration(configuredProvider));
        ReplaceTypedProvidersWithMockHttpClients(services, handler);

        using var rootProvider = services.BuildServiceProvider();
        using var scope = rootProvider.CreateScope();
        var completionProvider = scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>();

        if (configuredProvider == OllamaChatCompletionProvider.Name)
            Assert.IsType<OllamaChatCompletionProvider>(completionProvider);
        else
            Assert.IsType<GeminiChatCompletionProvider>(completionProvider);

        var item = new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Synthetic Fee Guide", "synthetic://fee-guide",
            "The filing fee is 5 demo credits.", 0.1, 0.9, 0.03, 1, 0.85, 1);
        var answerUseCase = new GroundedAnswerUseCase(
            new FixedTenantContext(Guid.NewGuid()),
            new FixedHybridSearchUseCase(item),
            completionProvider,
            new EvidenceSufficiencyPolicy(),
            new CitationValidator(),
            NullLogger<GroundedAnswerUseCase>.Instance);

        var result = await answerUseCase.GetGroundedAnswerAsync(new GroundedAnswerRequest("What is the filing fee?"), CancellationToken.None);

        Assert.Equal(GroundedAnswerStatus.Grounded, result.Status);
        Assert.Equal(expectedProvider, result.ProviderName);
        Assert.Equal("The filing fee is 5 demo credits [1].", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal("[1]", result.Citations[0].CitationId);
        Assert.Equal(item.ChunkId, result.Citations[0].ChunkId);
    }

    private static IConfiguration CreateConfiguration(string provider) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=test;Username=postgres;Password=test",
                ["LlmProviders:PrimaryProvider"] = provider,
                ["LlmProviders:PrimaryModel"] = provider == OllamaChatCompletionProvider.Name ? "llama3.2" : "gemini-2.5-flash",
                ["LlmProviders:OllamaBaseUrl"] = "http://ollama.test:11434",
                ["LlmProviders:HttpTimeoutSeconds"] = "30",
                ["LlmProviders:DefaultMaxOutputTokens"] = "256",
                ["LlmProviders:DefaultTemperature"] = "0.1"
            })
            .Build();

    private static void ReplaceTypedProvidersWithMockHttpClients(IServiceCollection services, HttpMessageHandler handler)
    {
        services.RemoveAll<GeminiChatCompletionProvider>();
        services.RemoveAll<OllamaChatCompletionProvider>();
        services.AddScoped(sp => new GeminiChatCompletionProvider(
            new HttpClient(handler),
            sp.GetRequiredService<IOptions<LlmProviderOptions>>(),
            NullLogger<GeminiChatCompletionProvider>.Instance));
        services.AddScoped(sp => new OllamaChatCompletionProvider(
            new HttpClient(handler),
            sp.GetRequiredService<IOptions<LlmProviderOptions>>(),
            NullLogger<OllamaChatCompletionProvider>.Instance));
    }

    private sealed class StaticResponseHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid GetTenantId() => tenantId;
    }

    private sealed class FixedHybridSearchUseCase(RerankResultItem item) : IHybridSearchUseCase
    {
        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new HybridSearchResponse(request.TopK ?? 5, 1, TimeSpan.Zero, "Test", "Test", [item]));
    }
}
