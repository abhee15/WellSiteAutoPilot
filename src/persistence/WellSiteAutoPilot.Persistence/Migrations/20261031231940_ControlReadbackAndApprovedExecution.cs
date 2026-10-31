using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ControlReadbackAndApprovedExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "control",
                table: "control_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Autonomous");

            migrationBuilder.AddColumn<decimal>(
                name: "ReadbackValue",
                schema: "control",
                table: "control_actions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                schema: "control",
                table: "control_actions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAtUtc",
                schema: "control",
                table: "control_actions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowApprovedRecommendations",
                schema: "control",
                table: "autonomy_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // control.manage is granted to the protected Admin role only; other roles opt in via the role catalog.
            migrationBuilder.Sql("""
                INSERT INTO security.role_permissions ("RoleId", "Permission") VALUES
                    ('b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010', 'control.manage')
                ON CONFLICT DO NOTHING;
                UPDATE security.role_definitions
                   SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
                 WHERE "Id" = 'b50cdb48-61e9-4c9f-a8d1-40dc1e8b7010';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM security.role_permissions WHERE "Permission" = 'control.manage';""");
            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "ReadbackValue",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "AllowApprovedRecommendations",
                schema: "control",
                table: "autonomy_policies");
        }
    }
}
