using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261007160000_AddExecutionRequestV2")]
public partial class AddExecutionRequestV2 : Migration
{
    private static readonly string[] ScheduledOccurrenceColumns =
        ["ConfigurationRevisionId", "ScheduledForUtc"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AlterColumn<Guid>(
            name: "AssetId",
            schema: "operations",
            table: "executions",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "AssetExternalId",
            schema: "operations",
            table: "executions",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "Quantity",
            schema: "operations",
            table: "executions",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);

        migrationBuilder.AddColumn<int>(
            name: "RequestContractVersion",
            schema: "operations",
            table: "executions",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "RequestPayloadJson",
            schema: "operations",
            table: "executions",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ScheduledForUtc",
            schema: "operations",
            table: "executions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Trigger",
            schema: "operations",
            table: "executions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Manual");

        migrationBuilder.CreateIndex(
            name: "IX_executions_ConfigurationRevisionId_ScheduledForUtc",
            schema: "operations",
            table: "executions",
            columns: ScheduledOccurrenceColumns,
            unique: true,
            filter: "\"ScheduledForUtc\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        throw new NotSupportedException(
            "Execution Request V2 is a forward-only migration because V2 executions do not have a lossless V1 single-input projection.");
    }
}
