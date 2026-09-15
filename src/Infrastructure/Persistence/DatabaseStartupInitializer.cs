using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovernmentDomainCopilot.Infrastructure.Persistence;

/// <summary>
/// Applies the existing EF Core migration history and creates the synthetic
/// development identities required by the local API-key authentication flow.
/// This initializer never resets or drops an existing database.
/// </summary>
public static class DatabaseStartupInitializer
{
    public static async Task InitializeAsync(
        GovernmentDomainCopilotDbContext dbContext,
        bool seedDevelopmentIdentities,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(logger);

        // Contract-test hosts use EF Core's in-memory provider. Migrations are
        // relational-only and should not make those isolated hosts depend on PostgreSQL.
        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying pending database migrations.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        if (!seedDevelopmentIdentities)
        {
            return;
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        foreach (var tenant in new[]
                 {
                     (SeedAuthIdentities.TenantAId, "Development Tenant A"),
                     (SeedAuthIdentities.TenantBId, "Development Tenant B")
                 })
        {
            if (!await dbContext.Tenants.AnyAsync(item => item.Id == tenant.Item1, cancellationToken))
            {
                dbContext.Tenants.Add(new Tenant(tenant.Item1, tenant.Item2, createdAtUtc));
            }
        }

        foreach (var identity in SeedAuthIdentities.AllUsers)
        {
            if (!await dbContext.Users.AnyAsync(item => item.Id == identity.UserId, cancellationToken))
            {
                dbContext.Users.Add(new User(
                    identity.UserId,
                    identity.TenantId,
                    identity.ExternalId,
                    identity.DisplayName,
                    createdAtUtc,
                    identity.Role));
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Initialized synthetic development tenant identities.");
        }
    }
}
