using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionMonitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailureCount",
                schema: "integration",
                table: "connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastTestSource",
                schema: "integration",
                table: "connections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextCheckAtUtc",
                schema: "integration",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveFailureCount",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastTestSource",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "NextCheckAtUtc",
                schema: "integration",
                table: "connections");
        }
    }
}
