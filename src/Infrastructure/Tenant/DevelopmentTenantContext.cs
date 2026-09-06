using GovernmentDomainCopilot.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace GovernmentDomainCopilot.Infrastructure.Tenancy;

/// <summary>
/// Development and testing implementation of <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// IMPORTANT: This is a development-only tenant context for the unauthenticated foundation phase.
/// Sourced from:
/// 1. HTTP request header <c>X-Tenant-ID</c> (used for multi-tenancy integration/contract testing).
/// 2. Configuration setting <c>Tenant:DevelopmentTenantId</c>.
/// 3. Default fallback GUID (<c>11111111-1111-1111-1111-111111111111</c>).
///
/// Under no circumstances is tenant identity accepted from client request DTO bodies.
/// Real authenticated identity claim resolution (e.g. JWT) will replace this in a future security phase.
/// </remarks>
public sealed class DevelopmentTenantContext : ITenantContext
{
    public const string HeaderName = "X-Tenant-ID";
    public static readonly Guid DefaultDevelopmentTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Guid _configuredTenantId;

    public DevelopmentTenantContext(
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

        var tenantIdString = configuration["Tenant:DevelopmentTenantId"];
        if (Guid.TryParse(tenantIdString, out var parsedConfigId) && parsedConfigId != Guid.Empty)
        {
            _configuredTenantId = parsedConfigId;
        }
        else
        {
            _configuredTenantId = DefaultDevelopmentTenantId;
        }
    }

    public Guid GetTenantId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            if (Guid.TryParse(headerValue.ToString(), out var headerTenantId) && headerTenantId != Guid.Empty)
            {
                return headerTenantId;
            }
        }

        return _configuredTenantId;
    }
}
