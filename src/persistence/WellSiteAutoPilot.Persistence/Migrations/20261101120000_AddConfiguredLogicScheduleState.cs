#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguredLogicScheduleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configured_logic_schedule_changes",
                schema: "logic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CadenceSeconds = table.Column<int>(type: "integer", nullable: false),
                    StartAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configured_logic_schedule_changes", x => x.Id);
                    table.CheckConstraint("CK_configured_logic_schedule_changes_cadence", "\"CadenceSeconds\" > 0");
                    table.CheckConstraint("CK_configured_logic_schedule_changes_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_configured_logic_schedule_changes_configured_logic_ConfiguredLogicId",
                        column: x => x.ConfiguredLogicId,
                        principalSchema: "logic",
                        principalTable: "configured_logic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configured_logic_schedule_states",
                schema: "logic",
                columns: table => new
                {
                    ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CadenceSeconds = table.Column<int>(type: "integer", nullable: false),
                    StartAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configured_logic_schedule_states", x => x.ConfiguredLogicId);
                    table.CheckConstraint("CK_configured_logic_schedule_states_cadence", "\"CadenceSeconds\" > 0");
                    table.CheckConstraint("CK_configured_logic_schedule_states_version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_configured_logic_schedule_states_configured_logic_ConfiguredLogicId",
                        column: x => x.ConfiguredLogicId,
                        principalSchema: "logic",
                        principalTable: "configured_logic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configured_logic_schedule_changes_ConfiguredLogicId_BasedOnRevisionId_Version",
                schema: "logic",
                table: "configured_logic_schedule_changes",
                columns: new[] { "ConfiguredLogicId", "BasedOnRevisionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configured_logic_schedule_changes_ConfiguredLogicId_ChangedAtUtc",
                schema: "logic",
                table: "configured_logic_schedule_changes",
                columns: new[] { "ConfiguredLogicId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configured_logic_schedule_changes",
                schema: "logic");

            migrationBuilder.DropTable(
                name: "configured_logic_schedule_states",
                schema: "logic");
        }
    }
}
