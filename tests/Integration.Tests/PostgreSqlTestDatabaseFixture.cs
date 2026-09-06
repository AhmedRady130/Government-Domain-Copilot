using System.Data.Common;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Integration.Tests;

public sealed class PostgreSqlTestDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgreSqlContainer;
    private DbConnection? _sqliteConnection;
    private string? _connectionString;

    public string ProviderName { get; private set; } = "PostgreSQL";

    public async Task InitializeAsync()
    {
        // 1. Check for explicit PostgreSQL connection string
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TestPostgreSQL");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            _connectionString = envConnectionString;
            ProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
            await InitializeDatabaseAsync();
            return;
        }

        // 2. Try Testcontainers for PostgreSQL if Docker is available
        try
        {
            _postgreSqlContainer = new PostgreSqlBuilder()
                .WithImage("pgvector/pgvector:pg16")
                .WithDatabase("test_government_domain_copilot")
                .WithUsername("test_user")
                .WithPassword("test_password_123!")
                .Build();

            await _postgreSqlContainer.StartAsync();
            _connectionString = _postgreSqlContainer.GetConnectionString();
            ProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
            await InitializeDatabaseAsync();
            return;
        }
        catch
        {
            // Testcontainers/Docker unavailable — fallback to Sqlite relational in-memory database
            _postgreSqlContainer = null;
        }

        // 3. Fallback: Relational SQLite in-memory provider enforcing real SQL constraints, transactions, FKs, and rollbacks
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        // Enable foreign key constraints in SQLite
        using (var command = _sqliteConnection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync();
        }

        ProviderName = "Microsoft.EntityFrameworkCore.Sqlite";
        await InitializeDatabaseAsync();
    }

    public GovernmentDomainCopilotDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<GovernmentDomainCopilotDbContext>();

        if (_connectionString != null && ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            optionsBuilder.UseNpgsql(_connectionString, npgsqlOptions => npgsqlOptions.UseVector());
        }
        else if (_sqliteConnection != null)
        {
            optionsBuilder.UseSqlite(_sqliteConnection);
        }
        else
        {
            throw new InvalidOperationException("No database provider connection initialized.");
        }

        return new GovernmentDomainCopilotDbContext(optionsBuilder.Options);
    }

    private async Task InitializeDatabaseAsync()
    {
        using var context = CreateDbContext();

        if (ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            // Run real EF Core migrations on PostgreSQL test database
            await context.Database.MigrateAsync();
        }
        else
        {
            // Ensure relational schema created for SQLite test database
            await context.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgreSqlContainer != null)
        {
            await _postgreSqlContainer.DisposeAsync();
        }

        if (_sqliteConnection != null)
        {
            await _sqliteConnection.DisposeAsync();
        }
    }
}
