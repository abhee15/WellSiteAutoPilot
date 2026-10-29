using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectoryQualifiedGroupGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_access_grants_GroupSid_RoleId_ScopeKind",
                schema: "security",
                table: "access_grants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_grants_subject",
                schema: "security",
                table: "access_grants");

            migrationBuilder.RenameColumn(
                name: "GroupSid",
                schema: "security",
                table: "access_grants",
                newName: "GroupId");

            migrationBuilder.RenameIndex(
                name: "IX_access_grants_GroupSid",
                schema: "security",
                table: "access_grants",
                newName: "IX_access_grants_GroupId");

            migrationBuilder.AlterColumn<string>(
                name: "GroupId", schema: "security", table: "access_grants",
                type: "character varying(256)", maxLength: 256, nullable: true, collation: "C",
                oldClrType: typeof(string), oldType: "character varying(256)", oldMaxLength: 256, oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectoryId",
                schema: "security",
                table: "access_grants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true, collation: "C");

            migrationBuilder.AddColumn<string>(
                name: "GroupDisplayName",
                schema: "security",
                table: "access_grants",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupDomain",
                schema: "security",
                table: "access_grants",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("UPDATE security.access_grants SET \"SubjectKind\" = 'Group', \"DirectoryId\" = 'windows-ad' WHERE \"SubjectKind\" = 'WindowsGroup';");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_DirectoryId_GroupId_RoleId_ScopeKind",
                schema: "security",
                table: "access_grants",
                columns: ["DirectoryId", "GroupId", "RoleId", "ScopeKind"],
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"SubjectKind\" = 'Group'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_grants_subject",
                schema: "security",
                table: "access_grants",
                sql: "\"SubjectKind\" = 'User' AND \"UserId\" IS NOT NULL AND \"GroupId\" IS NULL OR \"SubjectKind\" = 'Group' AND \"UserId\" IS NULL AND \"GroupId\" IS NOT NULL AND \"DirectoryId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM security.access_grants WHERE \"SubjectKind\" = 'Group' AND \"DirectoryId\" <> 'windows-ad') THEN RAISE EXCEPTION 'Cannot downgrade directory-qualified groups to Windows-only assignments'; END IF; END $$;");
            migrationBuilder.DropIndex(
                name: "IX_access_grants_DirectoryId_GroupId_RoleId_ScopeKind",
                schema: "security",
                table: "access_grants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_grants_subject",
                schema: "security",
                table: "access_grants");

            migrationBuilder.Sql("UPDATE security.access_grants SET \"SubjectKind\" = 'WindowsGroup' WHERE \"SubjectKind\" = 'Group';");

            migrationBuilder.DropColumn(
                name: "DirectoryId",
                schema: "security",
                table: "access_grants");

            migrationBuilder.DropColumn(
                name: "GroupDisplayName",
                schema: "security",
                table: "access_grants");

            migrationBuilder.DropColumn(
                name: "GroupDomain",
                schema: "security",
                table: "access_grants");

            migrationBuilder.AlterColumn<string>(
                name: "GroupId", schema: "security", table: "access_grants",
                type: "character varying(256)", maxLength: 256, nullable: true,
                oldClrType: typeof(string), oldType: "character varying(256)", oldMaxLength: 256,
                oldNullable: true, oldCollation: "C");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                schema: "security",
                table: "access_grants",
                newName: "GroupSid");

            migrationBuilder.RenameIndex(
                name: "IX_access_grants_GroupId",
                schema: "security",
                table: "access_grants",
                newName: "IX_access_grants_GroupSid");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_GroupSid_RoleId_ScopeKind",
                schema: "security",
                table: "access_grants",
                columns: ["GroupSid", "RoleId", "ScopeKind"],
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"SubjectKind\" = 'WindowsGroup'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_grants_subject",
                schema: "security",
                table: "access_grants",
                sql: "\"SubjectKind\" = 'User' AND \"UserId\" IS NOT NULL AND \"GroupSid\" IS NULL OR \"SubjectKind\" = 'WindowsGroup' AND \"UserId\" IS NULL AND \"GroupSid\" IS NOT NULL");
        }
    }
}
