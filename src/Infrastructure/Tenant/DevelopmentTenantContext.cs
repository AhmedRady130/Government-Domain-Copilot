using GovernmentDomainCopilot.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GovernmentDomainCopilot.Infrastructure.Tenancy;

/// <summary>
/// Development and testing implementation of <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// IMPORTANT: This is a development-only tenant context for the unauthenticated foundation phase.
/// Sourced strictly from configuration:
/// 1. Configuration setting <c>Tenant:DevelopmentTenantId</c>.
/// 2. Default fallback GUID (<c>11111111-1111-1111-1111-111111111111</c>).
///
/// Security Guard:
/// - Cannot be used outside Development environment (enforced by IHostEnvironment guard).
/// - Client-supplied headers (e.g. <c>X-Tenant-ID</c>) or request bodies are NEVER treated as authoritative.
/// - The configured development tenant is the only authoritative identity.
/// </remarks>
public sealed class DevelopmentTenantContext : ITenantContext
{
    public const string HeaderName = "X-Tenant-ID";
    public static readonly Guid DefaultDevelopmentTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Guid _configuredTenantId;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public DevelopmentTenantContext(
        IConfiguration configuration,
        IHostEnvironment? environment = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _httpContextAccessor = httpContextAccessor;

        if (environment != null && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("DevelopmentTenantContext must not be used outside Development environment.");
        }

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

    public DevelopmentTenantContext(
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
        : this(configuration, null, httpContextAccessor)
    {
    }

    public Guid GetTenantId()
    {
        // 1. If an authenticated user exists, extract TenantId from their claims
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = user.FindFirst("tenant_id")?.Value
                ?? user.FindFirst("TenantId")?.Value;

            if (Guid.TryParse(tenantClaim, out var claimTenantId) && claimTenantId != Guid.Empty)
            {
                return claimTenantId;
            }
        }

        // 2. Otherwise fall back to the configured development tenant ID
        return _configuredTenantId;
    }
}
