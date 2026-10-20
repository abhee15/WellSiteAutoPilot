using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>Records why, when and by whom an Asset Type was hidden. The existing inactive flag is the hidden state.</summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261020200000_AddAssetTypeHiding")]
public partial class AddAssetTypeHiding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<string>("HiddenNote", "asset_types", "character varying(200)", maxLength: 200,
            nullable: true, schema: "assets");
        migrationBuilder.AddColumn<System.DateTimeOffset>("HiddenAtUtc", "asset_types", "timestamp with time zone",
            nullable: true, schema: "assets");
        migrationBuilder.AddColumn<string>("HiddenBy", "asset_types", "character varying(256)", maxLength: 256,
            nullable: true, schema: "assets");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropColumn("HiddenBy", "asset_types", "assets");
        migrationBuilder.DropColumn("HiddenAtUtc", "asset_types", "assets");
        migrationBuilder.DropColumn("HiddenNote", "asset_types", "assets");
    }
}
