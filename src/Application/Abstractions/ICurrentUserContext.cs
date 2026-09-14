using System;
using System.Collections.Generic;

namespace GovernmentDomainCopilot.Application.Abstractions;

/// <summary>
/// Application-level abstraction for the current authenticated identity,
/// completely independent of ASP.NET Core ClaimsPrincipal.
/// </summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? ExternalId { get; }
    string? DisplayName { get; }
    Guid? TenantId { get; }
    IReadOnlyList<string> Roles { get; }

    bool IsInRole(string role);
}
