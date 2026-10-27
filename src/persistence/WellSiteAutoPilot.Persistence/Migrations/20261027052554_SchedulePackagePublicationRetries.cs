using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchedulePackagePublicationRetries : Migration
    {
        private static readonly string[] RetryIndexColumns = ["Type", "ProcessedAtUtc", "NextAttemptAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "messaging",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureCode",
                schema: "messaging",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                schema: "messaging",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Type_ProcessedAtUtc_NextAttemptAtUtc",
                schema: "messaging",
                table: "outbox_messages",
                columns: RetryIndexColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_Type_ProcessedAtUtc_NextAttemptAtUtc",
                schema: "messaging",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "messaging",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LastFailureCode",
                schema: "messaging",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "messaging",
                table: "outbox_messages");
        }
    }
}
