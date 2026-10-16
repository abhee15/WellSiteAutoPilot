using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261015022000_AddScopedAccessGrants")]
public partial class AddScopedAccessGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "security");
        migrationBuilder.CreateTable(
            name: "access_grants", schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                GroupSid = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                ScopeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_access_grants", row => row.Id);
                table.ForeignKey(
                    name: "FK_access_grants_users_UserId",
                    column: row => row.UserId,
                    principalSchema: "security", principalTable: "users",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_access_grants_role_definitions_RoleId",
                    column: row => row.RoleId,
                    principalSchema: "security", principalTable: "role_definitions",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint(
                    "CK_access_grants_subject",
                    "\"SubjectKind\" = 'User' AND \"UserId\" IS NOT NULL AND \"GroupSid\" IS NULL OR " +
                    "\"SubjectKind\" = 'WindowsGroup' AND \"UserId\" IS NULL AND \"GroupSid\" IS NOT NULL");
                table.CheckConstraint(
                    "CK_access_grants_scope_kind",
                    "\"ScopeKind\" IN ('ExactAssets', 'DeploymentWide', 'AllAssets')");
                table.CheckConstraint(
                    "CK_access_grants_revision", "\"Revision\" >= 1");
            });

        migrationBuilder.CreateTable(
            name: "assignment_asset_scopes", schema: "security",
            columns: table => new
            {
                GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assignment_asset_scopes", row => new { row.GrantId, row.AssetId });
                table.ForeignKey(
                    name: "FK_assignment_asset_scopes_access_grants_GrantId",
                    column: row => row.GrantId,
                    principalSchema: "security", principalTable: "access_grants",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_assignment_asset_scopes_assets_AssetId",
                    column: row => row.AssetId,
                    principalSchema: "assets", principalTable: "assets",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_access_grants_UserId",
            schema: "security", table: "access_grants", column: "UserId");
        migrationBuilder.CreateIndex(name: "IX_access_grants_GroupSid",
            schema: "security", table: "access_grants", column: "GroupSid");
        migrationBuilder.CreateIndex(name: "IX_access_grants_RoleId",
            schema: "security", table: "access_grants", column: "RoleId");
        migrationBuilder.CreateIndex(name: "IX_access_grants_UserId_RoleId_ScopeKind",
            schema: "security", table: "access_grants",
            columns: ["UserId", "RoleId", "ScopeKind"], unique: true,
            filter: "\"IsActive\" = TRUE AND \"SubjectKind\" = 'User'");
        migrationBuilder.CreateIndex(name: "IX_assignment_asset_scopes_AssetId",
            schema: "security", table: "assignment_asset_scopes", column: "AssetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: "assignment_asset_scopes", schema: "security");
        migrationBuilder.DropTable(name: "access_grants", schema: "security");
    }
}
