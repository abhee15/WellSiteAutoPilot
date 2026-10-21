using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Lets an owner share a View with everyone who can read Assets. Existing Views stay Personal. Adds the
/// deployment-wide permission to share: the protected Admin role holds every product permission and the Engineer
/// template may share; Operators may not.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261021210000_AddSharedAssetViews")]
public partial class AddSharedAssetViews : Migration
{
    private const string Admin = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010";
    private const string Engineer = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int>(
            name: "Visibility",
            table: "asset_views",
            schema: "assets",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_asset_views_NormalizedName_Shared",
            table: "asset_views",
            column: "NormalizedName",
            schema: "assets",
            unique: true,
            filter: "\"Visibility\" = 1");

        migrationBuilder.Sql($"""
            INSERT INTO security.role_permissions ("RoleId", "Permission") VALUES
                ('{Admin}', 'asset-views.share'),
                ('{Engineer}', 'asset-views.share')
            ON CONFLICT DO NOTHING;
            UPDATE security.role_definitions
               SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
             WHERE "Id" IN ('{Admin}', '{Engineer}');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            DELETE FROM security.role_permissions WHERE "Permission" = 'asset-views.share';
            """);
        migrationBuilder.DropIndex(name: "IX_asset_views_NormalizedName_Shared", table: "asset_views", schema: "assets");
        migrationBuilder.DropColumn(name: "Visibility", table: "asset_views", schema: "assets");
    }
}
