using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs the historical migration ordering: DocumentChunkKeywordSearch was
/// committed without migration metadata and is consequently not discovered by
/// EF Core. This discoverable migration is deliberately ordered before the
/// released SessionHistoryAndRunTraces migration, which creates the GIN index.
/// </summary>
[DbContext(typeof(GovernmentDomainCopilotDbContext))]
[Migration("20260906161600_AddMissingDocumentChunkSearchVector")]
public partial class AddMissingDocumentChunkSearchVector : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "DocumentChunks"
            ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
            GENERATED ALWAYS AS (to_tsvector('simple', coalesce("Content", ''))) STORED;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "DocumentChunks"
            DROP COLUMN IF EXISTS "SearchVector";
            """);
    }
}
