using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordControlActionTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "TargetAdapterKey",
                schema: "control",
                table: "control_actions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetCatalogVersion",
                schema: "control",
                table: "control_actions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetConnectionId",
                schema: "control",
                table: "control_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetConnectionRevision",
                schema: "control",
                table: "control_actions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetExternalId",
                schema: "control",
                table: "control_actions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "TargetAdapterKey",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "TargetCatalogVersion",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "TargetConnectionId",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "TargetConnectionRevision",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "TargetExternalId",
                schema: "control",
                table: "control_actions");
        }
    }
}
