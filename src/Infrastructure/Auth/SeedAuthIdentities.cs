using System;
using System.Collections.Generic;

namespace GovernmentDomainCopilot.Infrastructure.Auth;

public sealed class TestUserRecord
{
    public Guid UserId { get; init; }
    public Guid TenantId { get; init; }
    public string ExternalId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

/// <summary>
/// Deterministic synthetic test identities for >=2 tenants as required by T0 / FR-8.
/// Never contains real personal data or real production secrets.
/// </summary>
public static class SeedAuthIdentities
{
    public static readonly Guid TenantAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly TestUserRecord TenantAOfficer = new()
    {
        UserId = Guid.Parse("aaaa0001-0000-0000-0000-000000000001"),
        TenantId = TenantAId,
        ExternalId = "officer-a",
        DisplayName = "Tenant A Officer",
        Role = "Officer",
        ApiKey = "gov-key-tenant-a-officer"
    };

    public static readonly TestUserRecord TenantASupervisor = new()
    {
        UserId = Guid.Parse("aaaa0002-0000-0000-0000-000000000002"),
        TenantId = TenantAId,
        ExternalId = "supervisor-a",
        DisplayName = "Tenant A Supervisor",
        Role = "Supervisor",
        ApiKey = "gov-key-tenant-a-supervisor"
    };

    public static readonly TestUserRecord TenantBOfficer = new()
    {
        UserId = Guid.Parse("bbbb0001-0000-0000-0000-000000000001"),
        TenantId = TenantBId,
        ExternalId = "officer-b",
        DisplayName = "Tenant B Officer",
        Role = "Officer",
        ApiKey = "gov-key-tenant-b-officer"
    };

    public static readonly TestUserRecord TenantBSupervisor = new()
    {
        UserId = Guid.Parse("bbbb0002-0000-0000-0000-000000000002"),
        TenantId = TenantBId,
        ExternalId = "supervisor-b",
        DisplayName = "Tenant B Supervisor",
        Role = "Supervisor",
        ApiKey = "gov-key-tenant-b-supervisor"
    };

    public static readonly IReadOnlyList<TestUserRecord> AllUsers = new[]
    {
        TenantAOfficer,
        TenantASupervisor,
        TenantBOfficer,
        TenantBSupervisor
    };

    public static TestUserRecord? FindByApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        foreach (var user in AllUsers)
        {
            if (string.Equals(user.ApiKey, apiKey, StringComparison.Ordinal))
                return user;
        }
        return null;
    }
}
