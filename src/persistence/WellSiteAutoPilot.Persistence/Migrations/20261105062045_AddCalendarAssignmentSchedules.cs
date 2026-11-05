using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarAssignmentSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScheduleJson",
                schema: "logic",
                table: "configured_logic_schedule_states",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleJson",
                schema: "logic",
                table: "configured_logic_schedule_changes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduleJson",
                schema: "logic",
                table: "configured_logic_schedule_states");

            migrationBuilder.DropColumn(
                name: "ScheduleJson",
                schema: "logic",
                table: "configured_logic_schedule_changes");
        }
    }
}
