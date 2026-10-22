using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PinConfiguredLogicViewSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ViewConnectionId",
                schema: "logic",
                table: "configured_logic_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ViewId",
                schema: "logic",
                table: "configured_logic_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ViewRevision",
                schema: "logic",
                table: "configured_logic_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ViewRole",
                schema: "logic",
                table: "configured_logic_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_configured_logic_revisions_ViewId",
                schema: "logic",
                table: "configured_logic_revisions",
                column: "ViewId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_configured_logic_view_snapshot",
                schema: "logic",
                table: "configured_logic_revisions",
                sql: "(\"ViewId\" IS NULL AND \"ViewRevision\" IS NULL AND \"ViewConnectionId\" IS NULL AND \"ViewRole\" IS NULL) OR (\"ViewId\" IS NOT NULL AND \"ViewRevision\" IS NOT NULL AND \"ViewRevision\" > 0 AND \"ViewConnectionId\" IS NOT NULL AND \"ViewRole\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_configured_logic_revisions_asset_views_ViewId",
                schema: "logic",
                table: "configured_logic_revisions",
                column: "ViewId",
                principalSchema: "assets",
                principalTable: "asset_views",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_configured_logic_revisions_asset_views_ViewId",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropIndex(
                name: "IX_configured_logic_revisions_ViewId",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_configured_logic_view_snapshot",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropColumn(
                name: "ViewConnectionId",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropColumn(
                name: "ViewId",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropColumn(
                name: "ViewRevision",
                schema: "logic",
                table: "configured_logic_revisions");

            migrationBuilder.DropColumn(
                name: "ViewRole",
                schema: "logic",
                table: "configured_logic_revisions");
        }
    }
}
