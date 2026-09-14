using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObservabilityAndLlmTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmInvocationTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RunId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmInvocationTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LlmInvocationTraces_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmInvocationTraces_TenantId_CorrelationId",
                table: "LlmInvocationTraces",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmInvocationTraces_TenantId_RunId",
                table: "LlmInvocationTraces",
                columns: new[] { "TenantId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmInvocationTraces_TenantId_StartedAt",
                table: "LlmInvocationTraces",
                columns: new[] { "TenantId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunTraces_TenantId_CorrelationId",
                table: "OrchestrationRunTraces",
                columns: new[] { "TenantId", "CorrelationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmInvocationTraces");

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationRunTraces_TenantId_CorrelationId",
                table: "OrchestrationRunTraces");
        }
    }
}
