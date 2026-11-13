using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidedModuleDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO security.role_permissions ("RoleId", "Permission")
                SELECT "Id", 'logic.authoring.manage'
                  FROM security.role_definitions
                 WHERE "Key" = 'admin' AND "IsSystem" = TRUE
                ON CONFLICT DO NOTHING;
                UPDATE security.role_definitions
                   SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
                 WHERE "Key" = 'admin' AND "IsSystem" = TRUE;
                """);

            migrationBuilder.CreateTable(
                name: "guided_module_drafts",
                schema: "logic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_module_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guided_module_drafts_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guided_module_drafts_OwnerUserId",
                schema: "logic",
                table: "guided_module_drafts",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM security.role_permissions WHERE "Permission" = 'logic.authoring.manage';
                """);

            migrationBuilder.DropTable(
                name: "guided_module_drafts",
                schema: "logic");
        }
    }
}
