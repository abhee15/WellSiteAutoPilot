using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261006190000_AddAssetModel")]
public partial class AddAssetModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "assets");

        migrationBuilder.CreateTable(
            name: "asset_types",
            schema: "assets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                AttributeSchemaJson = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_asset_types", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "assets",
            schema: "assets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AssetTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ParentAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                AttributeValuesJson = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assets", x => x.Id);
                table.ForeignKey(
                    name: "FK_assets_asset_types_AssetTypeId",
                    column: x => x.AssetTypeId,
                    principalSchema: "assets",
                    principalTable: "asset_types",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_assets_assets_ParentAssetId",
                    column: x => x.ParentAssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_asset_types_Key",
            schema: "assets",
            table: "asset_types",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_assets_AssetTypeId",
            schema: "assets",
            table: "assets",
            column: "AssetTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_assets_ParentAssetId",
            schema: "assets",
            table: "assets",
            column: "ParentAssetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "assets", schema: "assets");
        migrationBuilder.DropTable(name: "asset_types", schema: "assets");
    }
}
