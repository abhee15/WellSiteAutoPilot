using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261014213000_AddRoleCatalog")]
public partial class AddRoleCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "security");
        migrationBuilder.CreateTable(
            name: "role_definitions",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_role_definitions", item => item.Id);
            });

        migrationBuilder.CreateTable(
            name: "role_permissions",
            schema: "security",
            columns: table => new
            {
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                Permission = table.Column<string>(
                    type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_role_permissions", item => new { item.RoleId, item.Permission });
                table.ForeignKey(
                    name: "FK_role_permissions_role_definitions_RoleId",
                    column: item => item.RoleId,
                    principalSchema: "security",
                    principalTable: "role_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_role_definitions_Key",
            schema: "security",
            table: "role_definitions",
            column: "Key",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_role_definitions_NormalizedName",
            schema: "security",
            table: "role_definitions",
            column: "NormalizedName",
            unique: true);

        // Fixed deployment-local IDs make system roles recoverable and auditable.
        // Their initial permission sets exactly mirror the old compiled RBAC map.
        var admin = Guid.Parse("b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010");
        var engineer = Guid.Parse("b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020");
        var operatorRole = Guid.Parse("b50cdb48-61e9-4c9f-a8d1-40dc1e8b7030");
        var seededAtUtc = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

        migrationBuilder.InsertData(
            schema: "security",
            table: "role_definitions",
            columns: ["Id", "Key", "Name", "NormalizedName", "Description",
                "IsSystem", "IsActive", "Revision", "CreatedAtUtc", "UpdatedAtUtc"],
            columnTypes: ["uuid", "character varying(128)", "character varying(80)",
                "character varying(80)", "character varying(500)", "boolean", "boolean",
                "bigint", "timestamp with time zone", "timestamp with time zone"],
            values: new object[,]
            {
                { admin, "admin", "Admin", "ADMIN", "Protected system administrator", true, true, 1L, seededAtUtc, seededAtUtc },
                { engineer, "engineer", "Engineer", "ENGINEER", "Protected system engineer template", true, true, 1L, seededAtUtc, seededAtUtc },
                { operatorRole, "operator", "Operator", "OPERATOR", "Protected system operator template", true, true, 1L, seededAtUtc, seededAtUtc }
            });

        var adminPermissions = new[]
        {
            "assets.read", "assets.manage", "logic.read", "logic.manage",
            "configured-logic.read", "configured-logic.manage",
            "executions.read", "executions.request",
            "recommendations.read", "recommendations.decide", "security.manage"
        };
        var engineerPermissions = adminPermissions
            .Where(item => item != "security.manage").ToArray();
        var operatorPermissions = new[]
        {
            "assets.read", "logic.read", "configured-logic.read",
            "executions.read", "recommendations.read", "recommendations.decide"
        };

        foreach (var (roleId, permissions) in new[]
        {
            (admin, adminPermissions),
            (engineer, engineerPermissions),
            (operatorRole, operatorPermissions)
        })
        {
            foreach (var permission in permissions)
            {
                migrationBuilder.InsertData(
                    schema: "security",
                    table: "role_permissions",
                    columns: ["RoleId", "Permission"],
                    columnTypes: ["uuid", "character varying(128)"],
                    values: [roleId, permission]);
            }
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "role_permissions", schema: "security");

        migrationBuilder.DropTable(
            name: "role_definitions", schema: "security");
    }
}
