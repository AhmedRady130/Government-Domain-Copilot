using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Tenancy;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Integration.Tests;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registers_the_PostgreSql_DbContext_without_secrets_or_connecting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] =
                    "Host=localhost;Database=government_domain_copilot;Username=postgres"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GovernmentDomainCopilotDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void AddInfrastructure_resolves_development_tenant_context_when_a_development_host_is_registered()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=government_domain_copilot;Username=postgres",
                ["Tenant:DevelopmentTenantId"] = tenantId.ToString()
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(Environments.Development));
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        Assert.IsType<DevelopmentTenantContext>(tenantContext);
        Assert.Equal(tenantId, tenantContext.GetTenantId());
    }

    [Fact]
    public void AddInfrastructure_does_not_resolve_development_tenant_context_in_production()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=government_domain_copilot;Username=postgres"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(Environments.Production));
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<GovernmentDomainCopilot.Infrastructure.Auth.CurrentUserContext>(scope.ServiceProvider.GetRequiredService<ITenantContext>());
    }

    [Fact]
    public void Cli_database_configuration_uses_an_explicit_standard_connection_string()
    {
        const string connectionString = "Host=postgres;Port=5432;Database=government_domain_copilot;Username=government_domain_copilot;Password=not-a-real-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GovernmentDomainCopilot"] = connectionString
            })
            .Build();

        Assert.True(DatabaseConnectionConfiguration.HasConfiguredConnectionString(configuration));
        Assert.Equal(connectionString, configuration.GetConnectionString("GovernmentDomainCopilot"));
    }

    [Fact]
    public void Cli_database_configuration_uses_in_memory_only_when_no_connection_string_is_configured()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(DatabaseConnectionConfiguration.HasConfiguredConnectionString(configuration));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GovernmentDomainCopilot";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
