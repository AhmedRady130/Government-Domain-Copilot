using Microsoft.Extensions.Configuration;

namespace GovernmentDomainCopilot.Infrastructure.Configuration;

/// <summary>Determines whether normal configuration supplies the application database connection.</summary>
public static class DatabaseConnectionConfiguration
{
    public static bool HasConfiguredConnectionString(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("GovernmentDomainCopilot"));
}
