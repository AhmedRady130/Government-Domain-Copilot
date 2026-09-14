using System.Security.Claims;
using GovernmentDomainCopilot.Domain.Constants;
using GovernmentDomainCopilot.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Integration.Tests.Answering;

public sealed class CurrentUserContextTests
{
    [Fact]
    public void GetTenantId_Unauthenticated_ThrowsInvalidOperationException()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = new CurrentUserContext(accessor);

        Assert.False(context.IsAuthenticated);
        Assert.Null(context.TenantId);
        Assert.Null(context.UserId);

        var ex = Assert.Throws<InvalidOperationException>(() => context.GetTenantId());
        Assert.Contains("No authenticated tenant context", ex.Message);
    }

    [Fact]
    public void GetTenantId_AuthenticatedWithoutTenantClaim_ThrowsInvalidOperationException()
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, Roles.Officer)
        }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = new CurrentUserContext(accessor);

        Assert.True(context.IsAuthenticated);
        Assert.Null(context.TenantId);

        var ex = Assert.Throws<InvalidOperationException>(() => context.GetTenantId());
        Assert.Contains("No authenticated tenant context", ex.Message);
    }

    [Fact]
    public void GetTenantId_AuthenticatedWithTenantClaim_ReturnsClaimTenantId()
    {
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();

        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, expectedUserId.ToString()),
            new Claim("tenant_id", expectedTenantId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Supervisor),
            new Claim(ClaimTypes.Name, "Test Supervisor")
        }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = new CurrentUserContext(accessor);

        Assert.True(context.IsAuthenticated);
        Assert.Equal(expectedTenantId, context.TenantId);
        Assert.Equal(expectedTenantId, context.GetTenantId());
        Assert.Equal(expectedUserId, context.UserId);
        Assert.True(context.IsInRole(Roles.Supervisor));
        Assert.False(context.IsInRole(Roles.Officer));
    }

    [Fact]
    public void GetTenantId_SpoofedHeader_IsIgnored_OnlyClaimIsUsed()
    {
        var claimTenantId = Guid.NewGuid();
        var spoofedHeaderTenantId = Guid.NewGuid();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-ID"] = spoofedHeaderTenantId.ToString();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", claimTenantId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Officer)
        }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = new CurrentUserContext(accessor);

        Assert.Equal(claimTenantId, context.GetTenantId());
        Assert.NotEqual(spoofedHeaderTenantId, context.GetTenantId());
    }
}
