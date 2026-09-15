using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.LLM.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests.Answering;

public sealed class LlmProviderDependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_SelectsGeminiByDefaultAndRegistersItScoped()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=test;Username=postgres;Password=test",
                ["LlmProviders:HttpTimeoutSeconds"] = "45",
                ["LlmProviders:DefaultMaxOutputTokens"] = "512",
                ["LlmProviders:DefaultTemperature"] = "0.2"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var rootProvider = services.BuildServiceProvider();

        // Verify that IChatCompletionProvider is registered as Scoped
        using var scope1 = rootProvider.CreateScope();
        using var scope2 = rootProvider.CreateScope();

        var providerScope1 = scope1.ServiceProvider.GetRequiredService<IChatCompletionProvider>();
        var providerScope2 = scope2.ServiceProvider.GetRequiredService<IChatCompletionProvider>();

        Assert.NotNull(providerScope1);
        Assert.NotNull(providerScope2);
        Assert.IsType<GeminiChatCompletionProvider>(providerScope1);
        Assert.IsType<GeminiChatCompletionProvider>(providerScope2);

        // Crucial: Scoped instances must be distinct per scope (not a single captured singleton)
        Assert.NotSame(providerScope1, providerScope2);
    }

    [Fact]
    public void AddInfrastructure_SelectsOllamaWhenConfigured()
    {
        var configuration = CreateConfiguration(OllamaChatCompletionProvider.Name);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<OllamaChatCompletionProvider>(scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>());
    }

    [Fact]
    public void AddInfrastructure_RejectsUnknownConfiguredProvider()
    {
        var configuration = CreateConfiguration("UnknownProvider");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>());
        Assert.Contains("UnknownProvider", ex.Message);
    }

    private static IConfiguration CreateConfiguration(string? primaryProvider = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=test;Username=postgres;Password=test",
                ["LlmProviders:PrimaryProvider"] = primaryProvider,
                ["LlmProviders:HttpTimeoutSeconds"] = "45",
                ["LlmProviders:DefaultMaxOutputTokens"] = "512",
                ["LlmProviders:DefaultTemperature"] = "0.2"
            })
            .Build();
}
