using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261008023000_AddRecommendations")]
public partial class AddRecommendations : Migration
{
    private static readonly string[] ExecutionIntentIndexColumns =
        ["ExecutionId", "IntentIndex"];

    private static readonly string[] StatusCreatedIndexColumns =
        ["Status", "CreatedAtUtc"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "recommendations",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                IntentIndex = table.Column<int>(type: "integer", nullable: false),
                ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                ConfigurationRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ModuleId = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                ModuleVersion = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                Command = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                Quantity = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true),
                SuggestedValue = table.Column<decimal>(
                    type: "numeric",
                    nullable: true),
                Unit = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true),
                ReasonCode = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                IntentJson = table.Column<string>(
                    type: "text",
                    nullable: false),
                Status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true),
                DecisionBy = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true),
                DecisionAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true),
                DecisionReason = table.Column<string>(
                    type: "character varying(1024)",
                    maxLength: 1024,
                    nullable: true),
                ControlActionId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recommendations", x => x.Id);
                table.ForeignKey(
                    name: "FK_recommendations_assets_AssetId",
                    column: x => x.AssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_recommendations_configured_logic_ConfiguredLogicId",
                    column: x => x.ConfiguredLogicId,
                    principalSchema: "logic",
                    principalTable: "configured_logic",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_recommendations_configured_logic_revisions_ConfigurationRevisionId",
                    column: x => x.ConfigurationRevisionId,
                    principalSchema: "logic",
                    principalTable: "configured_logic_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_recommendations_executions_ExecutionId",
                    column: x => x.ExecutionId,
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_AssetId",
            schema: "operations",
            table: "recommendations",
            column: "AssetId");

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_ConfiguredLogicId",
            schema: "operations",
            table: "recommendations",
            column: "ConfiguredLogicId");

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_ConfigurationRevisionId",
            schema: "operations",
            table: "recommendations",
            column: "ConfigurationRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_ExecutionId_IntentIndex",
            schema: "operations",
            table: "recommendations",
            columns: ExecutionIntentIndexColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_Status_CreatedAtUtc",
            schema: "operations",
            table: "recommendations",
            columns: StatusCreatedIndexColumns);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "recommendations",
            schema: "operations");
    }
}
