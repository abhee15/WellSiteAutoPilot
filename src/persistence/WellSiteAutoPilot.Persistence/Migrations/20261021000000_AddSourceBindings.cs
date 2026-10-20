using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

/// <summary>
/// Remembers which object in an outside system each imported Asset came from. Neither the connection nor the Asset
/// can be deleted while a binding refers to it.
/// </summary>
[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261021000000_AddSourceBindings")]
public partial class AddSourceBindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "source_bindings",
            schema: "integration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                ResourceKind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                SnapshotJson = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_bindings", item => item.Id);
                table.ForeignKey(
                    name: "FK_source_bindings_assets_AssetId",
                    column: item => item.AssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_source_bindings_connections_ConnectionId",
                    column: item => item.ConnectionId,
                    principalSchema: "integration",
                    principalTable: "connections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_source_bindings_AssetId",
            schema: "integration",
            table: "source_bindings",
            column: "AssetId");
        migrationBuilder.CreateIndex(
            name: "IX_source_bindings_ConnectionId_ResourceKind_ExternalId",
            schema: "integration",
            table: "source_bindings",
            columns: new[] { "ConnectionId", "ResourceKind", "ExternalId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: "source_bindings", schema: "integration");
    }
}
