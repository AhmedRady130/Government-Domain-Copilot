using GovernmentDomainCopilot.Infrastructure.Auth;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integration.Tests;

public sealed class DatabaseStartupInitializerTests
{
    [Fact]
    public async Task InitializeAsync_SeedsDevelopmentIdentitiesIdempotently()
    {
        var options = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new GovernmentDomainCopilotDbContext(options);

        await DatabaseStartupInitializer.InitializeAsync(
            dbContext,
            seedDevelopmentIdentities: true,
            NullLogger.Instance);
        await DatabaseStartupInitializer.InitializeAsync(
            dbContext,
            seedDevelopmentIdentities: true,
            NullLogger.Instance);

        Assert.Equal(2, await dbContext.Tenants.CountAsync());
        Assert.Equal(SeedAuthIdentities.AllUsers.Count, await dbContext.Users.CountAsync());
        Assert.Contains(await dbContext.Users.ToListAsync(), user => user.Id == SeedAuthIdentities.TenantAOfficer.UserId);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotSeedWhenDevelopmentSeedingIsDisabled()
    {
        var options = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new GovernmentDomainCopilotDbContext(options);

        await DatabaseStartupInitializer.InitializeAsync(
            dbContext,
            seedDevelopmentIdentities: false,
            NullLogger.Instance);

        Assert.Empty(await dbContext.Tenants.ToListAsync());
        Assert.Empty(await dbContext.Users.ToListAsync());
    }
}
