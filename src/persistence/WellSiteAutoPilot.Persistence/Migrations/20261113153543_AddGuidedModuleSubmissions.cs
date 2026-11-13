using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidedModuleSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO security.role_permissions ("RoleId", "Permission")
                SELECT "Id", 'logic.authoring.review'
                  FROM security.role_definitions
                 WHERE "Key" = 'admin' AND "IsSystem" = TRUE
                ON CONFLICT DO NOTHING;
                UPDATE security.role_definitions
                   SET "Revision" = "Revision" + 1, "UpdatedAtUtc" = now()
                 WHERE "Key" = 'admin' AND "IsSystem" = TRUE;
                """);

            migrationBuilder.CreateTable(
                name: "guided_module_submissions",
                schema: "logic",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_module_submissions", x => new { x.DraftId, x.Revision });
                    table.ForeignKey(
                        name: "FK_guided_module_submissions_guided_module_drafts_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "logic",
                        principalTable: "guided_module_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM security.role_permissions WHERE "Permission" = 'logic.authoring.review';
                """);

            migrationBuilder.DropTable(
                name: "guided_module_submissions",
                schema: "logic");
        }
    }
}
