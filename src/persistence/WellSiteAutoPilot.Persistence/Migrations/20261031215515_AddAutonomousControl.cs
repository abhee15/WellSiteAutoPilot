#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutonomousControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "control");

            migrationBuilder.CreateTable(
                name: "autonomy_policies",
                schema: "control",
                columns: table => new
                {
                    ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SinkKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LimitsJson = table.Column<string>(type: "text", nullable: false),
                    MaxActionsPerWindow = table.Column<int>(type: "integer", nullable: false),
                    WindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_autonomy_policies", x => x.ConfiguredLogicId);
                    table.ForeignKey(
                        name: "FK_autonomy_policies_configured_logic_ConfiguredLogicId",
                        column: x => x.ConfiguredLogicId,
                        principalSchema: "logic",
                        principalTable: "configured_logic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "control_actions",
                schema: "control",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfiguredLogicId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Command = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Quantity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    Unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GateCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sink = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SinkReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_control_actions_recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalSchema: "operations",
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kill_switch",
                schema: "control",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Engaged = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kill_switch", x => x.Id);
                    table.CheckConstraint("CK_kill_switch_singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                schema: "control",
                table: "kill_switch",
                columns: new[] { "Id", "ChangedAtUtc", "ChangedBy", "Engaged", "Reason" },
                values: new object[] { 1, null, null, false, null });

            migrationBuilder.CreateIndex(
                name: "IX_control_actions_ConfiguredLogicId_CreatedAtUtc",
                schema: "control",
                table: "control_actions",
                columns: new[] { "ConfiguredLogicId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_control_actions_RecommendationId",
                schema: "control",
                table: "control_actions",
                column: "RecommendationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autonomy_policies",
                schema: "control");

            migrationBuilder.DropTable(
                name: "control_actions",
                schema: "control");

            migrationBuilder.DropTable(
                name: "kill_switch",
                schema: "control");
        }
    }
}
