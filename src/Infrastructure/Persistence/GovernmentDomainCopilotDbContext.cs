using Microsoft.EntityFrameworkCore;

namespace GovernmentDomainCopilot.Infrastructure.Persistence;

/// <summary>
/// Represents the persistence boundary for the application.
/// Entity sets will be added only when a domain capability requires them.
/// </summary>
public sealed class GovernmentDomainCopilotDbContext(
    DbContextOptions<GovernmentDomainCopilotDbContext> options) : DbContext(options);
