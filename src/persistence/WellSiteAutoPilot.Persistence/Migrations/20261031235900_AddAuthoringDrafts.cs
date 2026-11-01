using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoringDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authoring");

            migrationBuilder.CreateTable(
                name: "drafts",
                schema: "authoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    RecordJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "draft_packages",
                schema: "authoring",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    Package = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_packages", x => new { x.DraftId, x.BuildId });
                    table.ForeignKey(
                        name: "FK_draft_packages_drafts_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "authoring",
                        principalTable: "drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_drafts_UpdatedAtUtc",
                schema: "authoring",
                table: "drafts",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_packages",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "drafts",
                schema: "authoring");
        }
    }
}
