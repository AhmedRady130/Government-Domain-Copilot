using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Tests;

public sealed class PgvectorTestDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgreSqlContainer;
    private string? _connectionString;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        // 1. Check for explicit PostgreSQL connection string
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TestPostgreSQL");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            _connectionString = envConnectionString;
            IsAvailable = true;
            await InitializeDatabaseAsync();
            return;
        }

        // 2. Try Testcontainers using pgvector-enabled image pgvector/pgvector:pg16
        try
        {
            _postgreSqlContainer = new PostgreSqlBuilder()
                .WithImage("pgvector/pgvector:pg16")
                .WithDatabase("test_government_domain_copilot_vector")
                .WithUsername("test_user")
                .WithPassword("test_password_123!")
                .Build();

            await _postgreSqlContainer.StartAsync();
            _connectionString = _postgreSqlContainer.GetConnectionString();
            IsAvailable = true;
            await InitializeDatabaseAsync();
        }
        catch
        {
            // Docker unavailable — pgvector tests will skip rather than fall back to SQLite
            _postgreSqlContainer = null;
            IsAvailable = false;
        }
    }

    public GovernmentDomainCopilotDbContext CreateDbContext()
    {
        if (!IsAvailable || _connectionString == null)
        {
            throw new InvalidOperationException("pgvector database connection is not available.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>();
        optionsBuilder.UseNpgsql(_connectionString, npgsqlOptions => npgsqlOptions.UseVector());

        return new GovernmentDomainCopilotDbContext(optionsBuilder.Options);
    }

    private async Task InitializeDatabaseAsync()
    {
        using var context = CreateDbContext();
        // Run EF Core migrations to enable vector extension, column, and HNSW index
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgreSqlContainer != null)
        {
            await _postgreSqlContainer.DisposeAsync();
        }
    }
}
