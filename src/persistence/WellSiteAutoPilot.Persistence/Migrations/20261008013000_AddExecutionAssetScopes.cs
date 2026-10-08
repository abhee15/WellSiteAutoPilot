using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261008013000_AddExecutionAssetScopes")]
public partial class AddExecutionAssetScopes : Migration
{
    private static readonly string[] LogicAssetScopeIndexColumns =
        ["LogicInstanceId", "AssetId"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "execution_asset_scopes",
            schema: "operations",
            columns: table => new
            {
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                LogicInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_execution_asset_scopes",
                    x => new { x.ExecutionId, x.AssetId });
                table.ForeignKey(
                    name: "FK_execution_asset_scopes_executions_ExecutionId",
                    column: x => x.ExecutionId,
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_execution_asset_scopes_LogicInstanceId_AssetId",
            schema: "operations",
            table: "execution_asset_scopes",
            columns: LogicAssetScopeIndexColumns);

        migrationBuilder.Sql(
            """
            INSERT INTO operations.execution_asset_scopes
                ("ExecutionId", "LogicInstanceId", "AssetId")
            SELECT
                "Id",
                "LogicInstanceId",
                "AssetId"
            FROM operations.executions
            WHERE "AssetId" IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);

        migrationBuilder.Sql(
            """
            INSERT INTO operations.execution_asset_scopes
                ("ExecutionId", "LogicInstanceId", "AssetId")
            SELECT
                execution."Id",
                execution."LogicInstanceId",
                (asset ->> 'assetId')::uuid
            FROM operations.executions AS execution
            CROSS JOIN LATERAL jsonb_array_elements(
                (execution."RequestPayloadJson"::jsonb) -> 'assets') AS asset
            WHERE execution."RequestContractVersion" = 2
              AND execution."RequestPayloadJson" IS NOT NULL
              AND jsonb_typeof(
                    (execution."RequestPayloadJson"::jsonb) -> 'assets') = 'array'
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "execution_asset_scopes",
            schema: "operations");
    }
}
