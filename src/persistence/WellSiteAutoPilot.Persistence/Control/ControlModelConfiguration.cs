using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Recommendations;

namespace WellSiteAutoPilot.Persistence.Control;

internal static class ControlModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var policy = modelBuilder.Entity<AutonomyPolicyEntity>();
        policy.ToTable("autonomy_policies", "control");
        policy.HasKey(x => x.ConfiguredLogicId);
        policy.Property(x => x.SinkKind).HasMaxLength(32).IsRequired();
        policy.Property(x => x.LimitsJson).IsRequired();
        policy.Property(x => x.UpdatedBy).HasMaxLength(256).IsRequired();
        policy.HasOne<ConfiguredLogicEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfiguredLogicId)
            .OnDelete(DeleteBehavior.Restrict);

        var action = modelBuilder.Entity<ControlActionEntity>();
        action.ToTable("control_actions", "control");
        action.HasKey(x => x.Id);
        action.Property(x => x.Command).HasMaxLength(256).IsRequired();
        action.Property(x => x.Quantity).HasMaxLength(256);
        action.Property(x => x.Unit).HasMaxLength(64);
        action.Property(x => x.Status).HasMaxLength(32).IsRequired();
        action.Property(x => x.GateCode).HasMaxLength(128).IsRequired();
        action.Property(x => x.Sink).HasMaxLength(32).IsRequired();
        action.Property(x => x.SinkReference).HasMaxLength(256);
        action.Property(x => x.ResolutionNote).HasMaxLength(1024);
        action.Property(x => x.Origin).HasMaxLength(32).HasDefaultValue("Autonomous").IsRequired();
        // One ControlAction per recommendation: duplicate delivery can never double-command.
        action.HasIndex(x => x.RecommendationId).IsUnique();
        action.HasIndex(x => new { x.ConfiguredLogicId, x.CreatedAtUtc });
        action.HasOne<RecommendationEntity>()
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);

        var killSwitch = modelBuilder.Entity<ControlKillSwitchEntity>();
        killSwitch.ToTable("kill_switch", "control",
            table => table.HasCheckConstraint("CK_kill_switch_singleton", "\"Id\" = 1"));
        killSwitch.HasKey(x => x.Id);
        killSwitch.Property(x => x.Id).ValueGeneratedNever();
        killSwitch.Property(x => x.ChangedBy).HasMaxLength(256);
        killSwitch.Property(x => x.Reason).HasMaxLength(1024);
        killSwitch.HasData(new ControlKillSwitchEntity { Id = 1, Engaged = false });
    }
}
