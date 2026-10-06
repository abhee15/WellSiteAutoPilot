using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261006233000_AddConfiguredLogic")]
public partial class AddConfiguredLogic : Migration
{
    private static readonly string[] RevisionIndexColumns =
        ["ConfiguredLogicId", "RevisionNumber"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "logic");

        migrationBuilder.CreateTable(
            name: "configured_logic",
            schema: "logic",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                ActiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_configured_logic", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "configured_logic_revisions",
            schema: "logic",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                ModuleId = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                ModuleVersion = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false),
                ModuleManifestJson = table.Column<string>(type: "text", nullable: false),
                Mode = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                ParametersJson = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                ValidatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true),
                ActivatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_configured_logic_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_configured_logic_revisions_configured_logic_ConfiguredLogicId",
                    column: x => x.ConfiguredLogicId,
                    principalSchema: "logic",
                    principalTable: "configured_logic",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "configured_logic_asset_bindings",
            schema: "logic",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                ParameterOverridesJson = table.Column<string>(
                    type: "text",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_configured_logic_asset_bindings",
                    x => new { x.RevisionId, x.Role, x.AssetId });
                table.ForeignKey(
                    name: "FK_configured_logic_asset_bindings_assets_AssetId",
                    column: x => x.AssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_configured_logic_asset_bindings_configured_logic_revisions_RevisionId",
                    column: x => x.RevisionId,
                    principalSchema: "logic",
                    principalTable: "configured_logic_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_configured_logic_ActiveRevisionId",
            schema: "logic",
            table: "configured_logic",
            column: "ActiveRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_configured_logic_asset_bindings_AssetId",
            schema: "logic",
            table: "configured_logic_asset_bindings",
            column: "AssetId");

        migrationBuilder.CreateIndex(
            name: "IX_configured_logic_revisions_ConfiguredLogicId_RevisionNumber",
            schema: "logic",
            table: "configured_logic_revisions",
            columns: RevisionIndexColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_configured_logic_revisions_Status",
            schema: "logic",
            table: "configured_logic_revisions",
            column: "Status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "configured_logic_asset_bindings",
            schema: "logic");

        migrationBuilder.DropTable(
            name: "configured_logic_revisions",
            schema: "logic");

        migrationBuilder.DropTable(
            name: "configured_logic",
            schema: "logic");
    }
}
