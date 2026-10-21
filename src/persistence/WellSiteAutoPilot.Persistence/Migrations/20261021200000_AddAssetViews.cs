using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>Personal, server-side Views: a saved query and arrangement over the Assets a person may already see.</summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261021200000_AddAssetViews")]
public partial class AddAssetViews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "asset_views",
            schema: "assets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                DefinitionJson = table.Column<string>(type: "text", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_asset_views", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_asset_views_OwnerUserId_NormalizedName",
            table: "asset_views",
            columns: ["OwnerUserId", "NormalizedName"],
            schema: "assets",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: "asset_views", schema: "assets");
    }
}
