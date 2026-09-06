using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentChunkKeywordSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "DocumentChunks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"Content\", ''))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_SearchVector",
                table: "DocumentChunks",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_SearchVector",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "DocumentChunks");
        }
    }
}
