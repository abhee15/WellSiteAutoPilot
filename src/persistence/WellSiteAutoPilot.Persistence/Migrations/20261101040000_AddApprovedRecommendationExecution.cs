using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedRecommendationExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Existing actions were all produced by the autonomy gate, so the default is the truthful backfill.
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "control",
                table: "control_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Autonomous");

            // Off for every existing policy: approved execution is an explicit per-configuration opt-in.
            migrationBuilder.AddColumn<bool>(
                name: "AllowApprovedRecommendations",
                schema: "control",
                table: "autonomy_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "control",
                table: "control_actions");

            migrationBuilder.DropColumn(
                name: "AllowApprovedRecommendations",
                schema: "control",
                table: "autonomy_policies");
        }
    }
}
