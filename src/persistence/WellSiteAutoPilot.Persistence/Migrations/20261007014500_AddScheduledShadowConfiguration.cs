using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261007014500_AddScheduledShadowConfiguration")]
public partial class AddScheduledShadowConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "ScheduleJson",
            schema: "logic",
            table: "configured_logic_revisions",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "configured_logic_data_bindings",
            schema: "logic",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                RequirementId = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderId = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                ProviderAssetExternalId = table.Column<string>(
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: false),
                ProviderMappingJson = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_configured_logic_data_bindings",
                    x => new { x.RevisionId, x.RequirementId, x.AssetId });
                table.ForeignKey(
                    name: "FK_configured_logic_data_bindings_assets_AssetId",
                    column: x => x.AssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_configured_logic_data_bindings_configured_logic_revisions_RevisionId",
                    column: x => x.RevisionId,
                    principalSchema: "logic",
                    principalTable: "configured_logic_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_configured_logic_data_bindings_AssetId",
            schema: "logic",
            table: "configured_logic_data_bindings",
            column: "AssetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "configured_logic_data_bindings",
            schema: "logic");

        migrationBuilder.DropColumn(
            name: "ScheduleJson",
            schema: "logic",
            table: "configured_logic_revisions");
    }
}
