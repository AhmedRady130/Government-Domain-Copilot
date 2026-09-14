using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using GovernmentDomainCopilot.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace GovernmentDomainCopilot.Infrastructure.Auth;

/// <summary>
/// Infrastructure adapter that maps the current authenticated ClaimsPrincipal
/// into the Application-level ICurrentUserContext and ITenantContext abstractions.
/// </summary>
public sealed class CurrentUserContext : ICurrentUserContext, ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var idClaim = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Principal?.FindFirst("sub")?.Value;

            return Guid.TryParse(idClaim, out var guid) ? guid : null;
        }
    }

    public string? ExternalId => Principal?.FindFirst("external_id")?.Value ?? Principal?.Identity?.Name;

    public string? DisplayName => Principal?.FindFirst(ClaimTypes.Name)?.Value ?? Principal?.FindFirst("name")?.Value;

    public Guid? TenantId
    {
        get
        {
            var tenantClaim = Principal?.FindFirst("tenant_id")?.Value
                ?? Principal?.FindFirst("TenantId")?.Value;

            return Guid.TryParse(tenantClaim, out var guid) ? guid : null;
        }
    }

    public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role)
        .Select(c => c.Value)
        .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool IsInRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        return Principal?.IsInRole(role) ?? false;
    }

    public Guid GetTenantId()
    {
        var tenant = TenantId;
        if (!tenant.HasValue || tenant.Value == Guid.Empty)
        {
            throw new InvalidOperationException("No authenticated tenant context is available in the current execution scope.");
        }

        return tenant.Value;
    }
}
