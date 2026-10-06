using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261006173000_AddExecutionRequests")]
public partial class AddExecutionRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(
            name: "operations");

        migrationBuilder.CreateTable(
            name: "executions",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LogicInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                ModuleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ModuleVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ConfigurationRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetExternalId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Quantity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ResultCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_executions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_executions_AssetId",
            schema: "operations",
            table: "executions",
            column: "AssetId");

        migrationBuilder.CreateIndex(
            name: "IX_executions_LogicInstanceId",
            schema: "operations",
            table: "executions",
            column: "LogicInstanceId");

        migrationBuilder.CreateIndex(
            name: "IX_executions_Status_RequestedAtUtc",
            schema: "operations",
            table: "executions",
            columns: new[] { "Status", "RequestedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "executions",
            schema: "operations");
    }
}
