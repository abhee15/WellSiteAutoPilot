using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration column arrays.

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionWellViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM security.role_permissions WHERE "Permission" = 'asset-views.share';
                INSERT INTO security.role_permissions ("RoleId", "Permission")
                SELECT r."Id", p.permission FROM security.role_definitions AS r
                CROSS JOIN (VALUES ('asset-views.create'), ('asset-views.edit'), ('asset-views.delete')) AS p(permission)
                WHERE r."Id" IN ('b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010', 'b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020')
                ON CONFLICT DO NOTHING;
                UPDATE security.role_definitions SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
                WHERE "Id" IN ('b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010', 'b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020');
                """);

            migrationBuilder.DropIndex(
                name: "IX_asset_views_NormalizedName_Shared",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.DropIndex(
                name: "IX_asset_views_OwnerUserId_NormalizedName",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                schema: "assets",
                table: "asset_views",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_view_members",
                schema: "assets",
                columns: table => new
                {
                    ViewId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_view_members", x => new { x.ViewId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_asset_view_members_asset_views_ViewId",
                        column: x => x.ViewId,
                        principalSchema: "assets",
                        principalTable: "asset_views",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_asset_view_members_assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_views_ConnectionId_NormalizedName",
                schema: "assets",
                table: "asset_views",
                columns: new[] { "ConnectionId", "NormalizedName" },
                unique: true,
                filter: "\"ConnectionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_asset_views_OwnerUserId_NormalizedName",
                schema: "assets",
                table: "asset_views",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true,
                filter: "\"ConnectionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_asset_view_members_AssetId",
                schema: "assets",
                table: "asset_view_members",
                column: "AssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_asset_views_connections_ConnectionId",
                schema: "assets",
                table: "asset_views",
                column: "ConnectionId",
                principalSchema: "integration",
                principalTable: "connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old version cannot represent explicit membership. Keep the original legacy definitions,
            // and remove new-format Views before restoring the old owner/global-name indexes.
            migrationBuilder.Sql("""
                DELETE FROM assets.asset_views WHERE "ConnectionId" IS NOT NULL;
                DELETE FROM security.role_permissions WHERE "Permission" IN ('asset-views.create', 'asset-views.edit', 'asset-views.delete');
                INSERT INTO security.role_permissions ("RoleId", "Permission")
                SELECT "Id", 'asset-views.share' FROM security.role_definitions
                WHERE "Id" IN ('b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010', 'b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020')
                ON CONFLICT DO NOTHING;
                UPDATE security.role_definitions SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
                WHERE "Id" IN ('b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010', 'b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_asset_views_connections_ConnectionId",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.DropTable(
                name: "asset_view_members",
                schema: "assets");

            migrationBuilder.DropIndex(
                name: "IX_asset_views_ConnectionId_NormalizedName",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.DropIndex(
                name: "IX_asset_views_OwnerUserId_NormalizedName",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "assets",
                table: "asset_views");

            migrationBuilder.CreateIndex(
                name: "IX_asset_views_NormalizedName_Shared",
                schema: "assets",
                table: "asset_views",
                column: "NormalizedName",
                unique: true,
                filter: "\"Visibility\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_asset_views_OwnerUserId_NormalizedName",
                schema: "assets",
                table: "asset_views",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true);
        }
    }
}
