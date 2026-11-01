using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControlReadback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

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
        }
    }
}
