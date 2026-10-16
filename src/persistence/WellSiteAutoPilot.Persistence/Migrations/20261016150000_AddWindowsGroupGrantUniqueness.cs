using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261016150000_AddWindowsGroupGrantUniqueness")]
public partial class AddWindowsGroupGrantUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: "IX_access_grants_GroupSid_RoleId_ScopeKind",
            schema: "security", table: "access_grants",
            columns: ["GroupSid", "RoleId", "ScopeKind"], unique: true,
            filter: "\"IsActive\" = TRUE AND \"SubjectKind\" = 'WindowsGroup'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropIndex(
            name: "IX_access_grants_GroupSid_RoleId_ScopeKind",
            schema: "security", table: "access_grants");
    }
}
