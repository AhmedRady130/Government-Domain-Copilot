using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairDocumentChunkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $EF$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'DocumentChunks'
                          AND column_name = 'SearchVector'
                    ) THEN
                        ALTER TABLE "DocumentChunks"
                        ADD COLUMN "SearchVector" tsvector
                        GENERATED ALWAYS AS (to_tsvector('simple', coalesce("Content", ''))) STORED;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = current_schema()
                          AND tablename = 'DocumentChunks'
                          AND indexname = 'IX_DocumentChunks_SearchVector'
                    ) THEN
                        CREATE INDEX "IX_DocumentChunks_SearchVector"
                        ON "DocumentChunks" USING gin ("SearchVector");
                    END IF;
                END $EF$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_DocumentChunks_SearchVector";
                ALTER TABLE "DocumentChunks" DROP COLUMN IF EXISTS "SearchVector";
                """);
        }
    }
}
