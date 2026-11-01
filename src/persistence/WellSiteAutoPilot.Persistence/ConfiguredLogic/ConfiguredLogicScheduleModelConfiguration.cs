using Microsoft.EntityFrameworkCore;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

internal static class ConfiguredLogicScheduleModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var state = modelBuilder.Entity<ConfiguredLogicScheduleStateEntity>();
        state.ToTable("configured_logic_schedule_states", "logic", table =>
        {
            table.HasCheckConstraint("CK_configured_logic_schedule_states_version", "\"Version\" > 0");
            table.HasCheckConstraint("CK_configured_logic_schedule_states_cadence", "\"CadenceSeconds\" > 0");
        });
        state.HasKey(x => x.ConfiguredLogicId);
        state.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        state.Property(x => x.ChangedBy).HasMaxLength(256).IsRequired();
        state.Property(x => x.Reason).HasMaxLength(1024);
        state.HasOne<ConfiguredLogicEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfiguredLogicId)
            .OnDelete(DeleteBehavior.Restrict);

        var change = modelBuilder.Entity<ConfiguredLogicScheduleChangeEntity>();
        change.ToTable("configured_logic_schedule_changes", "logic", table =>
        {
            table.HasCheckConstraint("CK_configured_logic_schedule_changes_version", "\"Version\" >= 0");
            table.HasCheckConstraint("CK_configured_logic_schedule_changes_cadence", "\"CadenceSeconds\" > 0");
        });
        change.HasKey(x => x.Id);
        change.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        change.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        change.Property(x => x.ChangedBy).HasMaxLength(256).IsRequired();
        change.Property(x => x.Reason).HasMaxLength(1024);
        // Versions count changes within one active revision; activation starts a new count.
        change.HasIndex(x => new { x.ConfiguredLogicId, x.BasedOnRevisionId, x.Version }).IsUnique();
        change.HasIndex(x => new { x.ConfiguredLogicId, x.ChangedAtUtc });
        change.HasOne<ConfiguredLogicEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfiguredLogicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
