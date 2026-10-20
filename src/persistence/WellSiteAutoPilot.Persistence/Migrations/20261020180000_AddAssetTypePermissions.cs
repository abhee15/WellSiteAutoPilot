using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Adds the deployment-wide asset-type permissions to the seeded system roles: the protected Admin role must
/// hold every product permission, and the Engineer template gains the ability to create types. Operators get
/// neither. Data only; there is no schema change.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261020180000_AddAssetTypePermissions")]
public partial class AddAssetTypePermissions : Migration
{
    private const string Admin = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010";
    private const string Engineer = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            INSERT INTO security.role_permissions ("RoleId", "Permission") VALUES
                ('{Admin}', 'asset-types.manage'),
                ('{Admin}', 'asset-types.hide'),
                ('{Engineer}', 'asset-types.manage')
            ON CONFLICT DO NOTHING;
            UPDATE security.role_definitions
               SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
             WHERE "Id" IN ('{Admin}', '{Engineer}');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            DELETE FROM security.role_permissions
             WHERE "Permission" IN ('asset-types.manage', 'asset-types.hide')
               AND "RoleId" IN ('{Admin}', '{Engineer}');
            """);
    }
}
