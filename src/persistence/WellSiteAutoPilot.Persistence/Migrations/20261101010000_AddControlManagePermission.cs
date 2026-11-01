using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Adds the deployment-wide control.manage permission to the protected Admin role, which must hold every product
/// permission. Other roles opt in through the role catalog; nothing else receives it. Data only; there is no schema
/// change, so the model snapshot is unchanged.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261101010000_AddControlManagePermission")]
public partial class AddControlManagePermission : Migration
{
    private const string Admin = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            INSERT INTO security.role_permissions ("RoleId", "Permission") VALUES
                ('{Admin}', 'control.manage')
            ON CONFLICT DO NOTHING;
            UPDATE security.role_definitions
               SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
             WHERE "Id" = '{Admin}';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            DELETE FROM security.role_permissions WHERE "Permission" = 'control.manage';
            """);
    }
}
