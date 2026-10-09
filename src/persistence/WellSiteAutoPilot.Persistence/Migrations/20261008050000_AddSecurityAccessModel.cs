using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
[Migration("20261008050000_AddSecurityAccessModel")]
public partial class AddSecurityAccessModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.EnsureSchema(name: "security");
        migrationBuilder.EnsureSchema(name: "audit");

        migrationBuilder.CreateTable(
            name: "audit_events",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                ActorUserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                ActorIdentity = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                Action = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                TargetType = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                TargetId = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true),
                AssetId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                CorrelationId = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true),
                DetailsJson = table.Column<string>(
                    type: "text",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IdentityName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                NormalizedIdentityName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false),
                DisplayName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true),
                IsActive = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            schema: "security",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.Role });
                table.ForeignKey(
                    name: "FK_user_roles_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_asset_scopes",
            schema: "security",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_user_asset_scopes",
                    x => new { x.UserId, x.AssetId });
                table.ForeignKey(
                    name: "FK_user_asset_scopes_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_asset_scopes_assets_AssetId",
                    column: x => x.AssetId,
                    principalSchema: "assets",
                    principalTable: "assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_Action",
            schema: "audit",
            table: "audit_events",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_ActorUserId",
            schema: "audit",
            table: "audit_events",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_AssetId",
            schema: "audit",
            table: "audit_events",
            column: "AssetId");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_OccurredAtUtc",
            schema: "audit",
            table: "audit_events",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_users_NormalizedIdentityName",
            schema: "security",
            table: "users",
            column: "NormalizedIdentityName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_roles_Role",
            schema: "security",
            table: "user_roles",
            column: "Role");

        migrationBuilder.CreateIndex(
            name: "IX_user_asset_scopes_AssetId",
            schema: "security",
            table: "user_asset_scopes",
            column: "AssetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "user_asset_scopes",
            schema: "security");
        migrationBuilder.DropTable(
            name: "user_roles",
            schema: "security");
        migrationBuilder.DropTable(
            name: "users",
            schema: "security");
        migrationBuilder.DropTable(
            name: "audit_events",
            schema: "audit");
    }
}
