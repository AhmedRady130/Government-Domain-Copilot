using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace Integration.Tests.Persistence;

/// <summary>
/// Runs the complete migration chain against a fresh pgvector-enabled PostgreSQL database.
/// The fixture intentionally has no in-memory fallback for PostgreSQL-specific assertions.
/// </summary>
public sealed class DocumentChunkSearchVectorMigrationTests : IClassFixture<PgvectorTestDatabaseFixture>
{
    private readonly PgvectorTestDatabaseFixture _fixture;

    public DocumentChunkSearchVectorMigrationTests(PgvectorTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fresh_database_applies_all_migrations_and_keyword_search_uses_the_search_vector_index()
    {
        if (!_fixture.IsAvailable)
        {
            throw SkipException.ForSkip(
                "PostgreSQL/pgvector Testcontainers is unavailable; the migration regression test was not executed.");
        }

        await using var context = _fixture.CreateDbContext();

        var expectedMigrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(expectedMigrations, appliedMigrations);

        await context.Database.OpenConnectionAsync();
        try
        {
            await using var columnCommand = context.Database.GetDbConnection().CreateCommand();
            columnCommand.CommandText =
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns " +
                "WHERE table_schema = current_schema() " +
                "AND table_name = 'DocumentChunks' " +
                "AND column_name = 'SearchVector');";
            Assert.True((bool)(await columnCommand.ExecuteScalarAsync())!);

            await using var indexCommand = context.Database.GetDbConnection().CreateCommand();
            indexCommand.CommandText =
                "SELECT EXISTS (SELECT 1 FROM pg_indexes " +
                "WHERE schemaname = current_schema() " +
                "AND tablename = 'DocumentChunks' " +
                "AND indexname = 'IX_DocumentChunks_SearchVector');";
            Assert.True((bool)(await indexCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        var tenant = new Tenant(Guid.NewGuid(), "Migration test tenant", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(),
            tenant.Id,
            "Keyword migration test document",
            $"migration-test-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(
            Guid.NewGuid(),
            tenant.Id,
            document.Id,
            0,
            "The migration regression keyword must be searchable.");

        var repository = new DocumentRepository(context);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        await repository.SaveAsync(document, new[] { chunk }, CancellationToken.None);

        var results = await new PgKeywordChunkRetriever(context)
            .SearchKeywordAsync(tenant.Id, "regression keyword", 10, CancellationToken.None);

        Assert.Contains(results, result => result.ChunkId == chunk.Id);
    }
}
