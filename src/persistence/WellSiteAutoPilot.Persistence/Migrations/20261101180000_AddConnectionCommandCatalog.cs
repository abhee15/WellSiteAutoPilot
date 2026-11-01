#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionCommandCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connection_command_catalog_changes",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connection_command_catalog_changes", x => x.Id);
                    table.CheckConstraint("CK_connection_command_catalog_changes_version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_connection_command_catalog_changes_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connection_command_catalogs",
                schema: "integration",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connection_command_catalogs", x => x.ConnectionId);
                    table.CheckConstraint("CK_connection_command_catalogs_version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_connection_command_catalogs_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connection_command_catalog_entries",
                schema: "integration",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Command = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Minimum = table.Column<decimal>(type: "numeric", nullable: true),
                    Maximum = table.Column<decimal>(type: "numeric", nullable: true),
                    MaximumStep = table.Column<decimal>(type: "numeric", nullable: true),
                    TargetJson = table.Column<string>(type: "text", nullable: false),
                    SettleSeconds = table.Column<int>(type: "integer", nullable: false),
                    DeadlineSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connection_command_catalog_entries", x => new { x.ConnectionId, x.Command });
                    table.CheckConstraint("CK_connection_command_catalog_entries_limits", "NOT \"Enabled\" OR (\"Minimum\" IS NOT NULL AND \"Maximum\" IS NOT NULL AND \"Minimum\" <= \"Maximum\")");
                    table.CheckConstraint("CK_connection_command_catalog_entries_step", "\"MaximumStep\" IS NULL OR \"MaximumStep\" > 0");
                    table.CheckConstraint("CK_connection_command_catalog_entries_timing", "\"SettleSeconds\" >= 0 AND \"DeadlineSeconds\" > \"SettleSeconds\"");
                    table.ForeignKey(
                        name: "FK_connection_command_catalog_entries_connection_command_catalogs_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connection_command_catalogs",
                        principalColumn: "ConnectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connection_command_catalog_changes_ConnectionId_Version",
                schema: "integration",
                table: "connection_command_catalog_changes",
                columns: new[] { "ConnectionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connection_command_catalog_changes",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "connection_command_catalog_entries",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "connection_command_catalogs",
                schema: "integration");
        }
    }
}
