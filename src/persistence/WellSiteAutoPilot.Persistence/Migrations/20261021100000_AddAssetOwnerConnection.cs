using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Gives each Asset (and Group) at most one owning Connection. Existing imports are filled in from their source
/// bindings; a Group is owned only when everything beneath it belongs to one Connection, so nothing is guessed.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261021100000_AddAssetOwnerConnection")]
public partial class AddAssetOwnerConnection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<Guid>("OwnerConnectionId", "assets", "uuid", nullable: true, schema: "assets");
        migrationBuilder.CreateIndex("IX_assets_OwnerConnectionId", "assets", "OwnerConnectionId", schema: "assets");
        migrationBuilder.AddForeignKey(
            name: "FK_assets_connections_OwnerConnectionId",
            table: "assets",
            column: "OwnerConnectionId",
            principalSchema: "integration",
            principalTable: "connections",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict,
            schema: "assets");

        migrationBuilder.Sql("""
            UPDATE assets.assets AS a SET "OwnerConnectionId" = b."ConnectionId"
            FROM integration.source_bindings AS b WHERE b."AssetId" = a."Id";
            """);
        migrationBuilder.Sql("""
            WITH RECURSIVE below AS (
                SELECT parent."Id" AS "RootId", child."Id" AS "NodeId", child."OwnerConnectionId" AS "Owner",
                       NOT EXISTS (SELECT 1 FROM assets.assets AS x WHERE x."ParentAssetId" = child."Id") AS "IsLeaf"
                FROM assets.assets AS parent JOIN assets.assets AS child ON child."ParentAssetId" = parent."Id"
                UNION ALL
                SELECT below."RootId", child."Id", child."OwnerConnectionId",
                       NOT EXISTS (SELECT 1 FROM assets.assets AS x WHERE x."ParentAssetId" = child."Id")
                FROM below JOIN assets.assets AS child ON child."ParentAssetId" = below."NodeId"),
            single_owner AS (
                SELECT "RootId", MIN("Owner"::text)::uuid AS "Owner" FROM below
                GROUP BY "RootId"
                HAVING COUNT(*) FILTER (WHERE "IsLeaf") = COUNT("Owner") FILTER (WHERE "IsLeaf")
                   AND COUNT(DISTINCT "Owner") FILTER (WHERE "IsLeaf") = 1)
            UPDATE assets.assets AS g SET "OwnerConnectionId" = single_owner."Owner"
            FROM single_owner WHERE g."Id" = single_owner."RootId" AND g."OwnerConnectionId" IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropForeignKey("FK_assets_connections_OwnerConnectionId", "assets", "assets");
        migrationBuilder.DropIndex("IX_assets_OwnerConnectionId", "assets", "assets");
        migrationBuilder.DropColumn("OwnerConnectionId", "assets", "assets");
    }
}
