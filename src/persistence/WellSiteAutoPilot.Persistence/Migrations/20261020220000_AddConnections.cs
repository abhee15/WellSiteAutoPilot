using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Adds the saved connections to outside systems and the two deployment-wide permissions that govern them. The
/// protected Admin role holds every product permission and the Engineer template manages connections; Operators
/// get neither.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261020220000_AddConnections")]
public partial class AddConnections : Migration
{
    private const string Admin = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010";
    private const string Engineer = "b50cdb48-61e9-4c9f-a8d1-40dc1e8b7020";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "integration");
        migrationBuilder.CreateTable(
            name: "connections",
            schema: "integration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AdapterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SettingsVersion = table.Column<int>(type: "integer", nullable: false),
                SettingsJson = table.Column<string>(type: "text", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastTestOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                LastTestMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LastTestActingAs = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                LastTestAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastTestRevision = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_connections", item => item.Id);
            });
        migrationBuilder.CreateIndex(
            name: "IX_connections_NormalizedName",
            schema: "integration",
            table: "connections",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.Sql($"""
            INSERT INTO security.role_permissions ("RoleId", "Permission") VALUES
                ('{Admin}', 'connections.read'),
                ('{Admin}', 'connections.manage'),
                ('{Engineer}', 'connections.read'),
                ('{Engineer}', 'connections.manage')
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
             WHERE "Permission" IN ('connections.read', 'connections.manage')
               AND "RoleId" IN ('{Admin}', '{Engineer}');
            """);
        migrationBuilder.DropTable(name: "connections", schema: "integration");
    }
}
