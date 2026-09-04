using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GovernmentDomainCopilot.Infrastructure.Persistence;

public sealed class GovernmentDomainCopilotDbContextFactory
    : IDesignTimeDbContextFactory<GovernmentDomainCopilotDbContext>
{
    public GovernmentDomainCopilotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__GovernmentDomainCopilot")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__GovernmentDomainCopilot before running EF Core tooling.");

        var options = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GovernmentDomainCopilotDbContext(options);
    }
}
