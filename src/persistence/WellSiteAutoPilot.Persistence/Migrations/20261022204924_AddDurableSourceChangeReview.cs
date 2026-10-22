using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableSourceChangeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_checks",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ConnectionRevision = table.Column<int>(type: "integer", nullable: false),
                    HierarchyRevision = table.Column<int>(type: "integer", nullable: true),
                    AcceptedBindingsFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActingAs = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceScopeJson = table.Column<string>(type: "text", nullable: true),
                    Found = table.Column<int>(type: "integer", nullable: false),
                    Changed = table.Column<int>(type: "integer", nullable: false),
                    New = table.Column<int>(type: "integer", nullable: false),
                    NotReturned = table.Column<int>(type: "integer", nullable: false),
                    ComparisonUnavailable = table.Column<int>(type: "integer", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: false),
                    UnavailableFieldsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_checks_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_changes",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceKind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    AcceptedBaselineJson = table.Column<string>(type: "text", nullable: true),
                    ObservedFactsJson = table.Column<string>(type: "text", nullable: true),
                    FirstCheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastCheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_changes_assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_changes_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integration",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_changes_source_checks_FirstCheckId",
                        column: x => x.FirstCheckId,
                        principalSchema: "integration",
                        principalTable: "source_checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_changes_source_checks_LastCheckId",
                        column: x => x.LastCheckId,
                        principalSchema: "integration",
                        principalTable: "source_checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_change_impacts",
                schema: "integration",
                columns: table => new
                {
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_change_impacts", x => new { x.ChangeId, x.RevisionId, x.Kind, x.Reference });
                    table.ForeignKey(
                        name: "FK_source_change_impacts_configured_logic_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalSchema: "logic",
                        principalTable: "configured_logic_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_change_impacts_source_changes_ChangeId",
                        column: x => x.ChangeId,
                        principalSchema: "integration",
                        principalTable: "source_changes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_change_impacts_RevisionId",
                schema: "integration",
                table: "source_change_impacts",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_source_changes_AssetId",
                schema: "integration",
                table: "source_changes",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_source_changes_ConnectionId_LastSeenAtUtc",
                schema: "integration",
                table: "source_changes",
                columns: ["ConnectionId", "LastSeenAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_source_changes_ConnectionId_ResourceKind_ExternalId_Fingerp~",
                schema: "integration",
                table: "source_changes",
                columns: ["ConnectionId", "ResourceKind", "ExternalId", "Fingerprint"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_changes_FirstCheckId",
                schema: "integration",
                table: "source_changes",
                column: "FirstCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_source_changes_LastCheckId",
                schema: "integration",
                table: "source_changes",
                column: "LastCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_source_checks_ConnectionId_Revision",
                schema: "integration",
                table: "source_checks",
                columns: ["ConnectionId", "Revision"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_change_impacts",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "source_changes",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "source_checks",
                schema: "integration");
        }
    }
}
