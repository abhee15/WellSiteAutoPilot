using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261007113000_AddLogicModuleCatalog")]
public partial class AddLogicModuleCatalog : Migration
{
    private static readonly string[] ModuleVersionColumns = ["ModuleId", "Version"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "logic_modules",
            schema: "logic",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ModuleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Publisher = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Runtime = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExecutionProfile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ManifestJson = table.Column<string>(type: "text", nullable: false),
                PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TrustStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                InstalledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_logic_modules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_logic_modules_ModuleId_Version",
            schema: "logic",
            table: "logic_modules",
            columns: ModuleVersionColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_logic_modules_TrustStatus",
            schema: "logic",
            table: "logic_modules",
            column: "TrustStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "logic_modules",
            schema: "logic");
    }
}
