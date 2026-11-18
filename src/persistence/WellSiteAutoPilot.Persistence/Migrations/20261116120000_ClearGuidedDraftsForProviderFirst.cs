using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations
{
    /// <summary>
    /// Provider-first authoring: every guided draft now names its primary provider. Existing drafts (and their
    /// submissions) carry none and there is no live data to keep, so they are removed instead of guessing one.
    /// The schema is unchanged; the provider lives in the draft's manifest JSON.
    /// </summary>
    public partial class ClearGuidedDraftsForProviderFirst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM logic.guided_module_submissions;
                DELETE FROM logic.guided_module_drafts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
