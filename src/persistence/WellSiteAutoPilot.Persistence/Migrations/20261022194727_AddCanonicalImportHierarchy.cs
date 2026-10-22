using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalImportHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_hierarchies",
                schema: "integration",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LevelsJson = table.Column<string>(type: "text", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_hierarchies", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_import_hierarchies_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "imported_groups",
                schema: "integration",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceLabel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_groups", x => new { x.ConnectionId, x.GroupKey });
                    table.ForeignKey(
                        name: "FK_imported_groups_assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_imported_groups_import_hierarchies_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "import_hierarchies",
                        principalColumn: "ConnectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_imported_groups_AssetId",
                schema: "integration",
                table: "imported_groups",
                column: "AssetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "imported_groups",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "import_hierarchies",
                schema: "integration");
        }
    }
}
