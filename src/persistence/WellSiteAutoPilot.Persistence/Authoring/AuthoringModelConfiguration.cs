using Microsoft.EntityFrameworkCore;

namespace WellSiteAutoPilot.Persistence.Authoring;

internal static class AuthoringModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var draft = modelBuilder.Entity<AuthoringDraftEntity>();
        draft.ToTable("drafts", "authoring");
        draft.HasKey(x => x.Id);
        draft.Property(x => x.RecordJson).IsRequired();
        draft.Property(x => x.Version).IsConcurrencyToken();
        draft.HasIndex(x => x.UpdatedAtUtc);

        var package = modelBuilder.Entity<AuthoringPackageEntity>();
        package.ToTable("draft_packages", "authoring");
        package.HasKey(x => new { x.DraftId, x.BuildId });
        package.Property(x => x.Package).IsRequired();
        package.HasOne<AuthoringDraftEntity>()
            .WithMany()
            .HasForeignKey(x => x.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
