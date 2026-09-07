using GovernmentDomainCopilot.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests.Answering;

public sealed class DevelopmentTenantContextTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Constructor_OutsideDevelopmentEnvironment_ThrowsInvalidOperationException(string envName)
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment(envName);

        var ex = Assert.Throws<InvalidOperationException>(() => new DevelopmentTenantContext(config, env));
        Assert.Contains("Development environment", ex.Message);
    }

    [Fact]
    public void Constructor_InDevelopmentEnvironment_Succeeds()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment("Development");

        var context = new DevelopmentTenantContext(config, env);
        Assert.Equal(DevelopmentTenantContext.DefaultDevelopmentTenantId, context.GetTenantId());
    }

    [Fact]
    public void GetTenantId_ClientSuppliedHeader_DoesNotOverrideConfiguredTenantIdentity()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment("Development");

        var httpContext = new DefaultHttpContext();
        var spoofedTenantId = Guid.NewGuid();
        httpContext.Request.Headers[DevelopmentTenantContext.HeaderName] = spoofedTenantId.ToString();

        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = new DevelopmentTenantContext(config, env, httpContextAccessor);

        // Security check: must return configured development tenant, NEVER the spoofed header ID
        Assert.NotEqual(spoofedTenantId, context.GetTenantId());
        Assert.Equal(DevelopmentTenantContext.DefaultDevelopmentTenantId, context.GetTenantId());
    }

    [Fact]
    public void GetTenantId_ReadsConfiguredTenantId_WhenSpecifiedInConfiguration()
    {
        var expectedId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:DevelopmentTenantId"] = expectedId.ToString()
            })
            .Build();

        var env = new FakeHostEnvironment("Development");
        var context = new DevelopmentTenantContext(config, env);

        Assert.Equal(expectedId, context.GetTenantId());
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "GovernmentDomainCopilot";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;

        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }
    }
}
