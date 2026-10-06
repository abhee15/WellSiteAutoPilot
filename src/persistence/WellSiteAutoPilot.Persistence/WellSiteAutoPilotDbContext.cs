using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence;

public sealed class WellSiteAutoPilotDbContext(DbContextOptions<WellSiteAutoPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<ExecutionEntity> Executions => Set<ExecutionEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var execution = modelBuilder.Entity<ExecutionEntity>();
        execution.ToTable("executions", "operations");
        execution.HasKey(x => x.Id);
        execution.Property(x => x.ModuleId).HasMaxLength(256).IsRequired();
        execution.Property(x => x.ModuleVersion).HasMaxLength(64).IsRequired();
        execution.Property(x => x.AssetExternalId).HasMaxLength(512).IsRequired();
        execution.Property(x => x.Quantity).HasMaxLength(256).IsRequired();
        execution.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        execution.Property(x => x.Status).HasMaxLength(32).IsRequired();
        execution.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        execution.Property(x => x.RequestedAtUtc).IsRequired();
        execution.Property(x => x.ResultCode).HasMaxLength(128);
        execution.Property(x => x.FailureCode).HasMaxLength(128);
        execution.HasIndex(x => x.LogicInstanceId);
        execution.HasIndex(x => x.AssetId);
        execution.HasIndex(x => new { x.Status, x.RequestedAtUtc });

        var outbox = modelBuilder.Entity<OutboxMessageEntity>();
        outbox.ToTable("outbox_messages", "messaging");
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Type).HasMaxLength(256).IsRequired();
        outbox.Property(x => x.Payload).IsRequired();
        outbox.Property(x => x.CreatedAtUtc).IsRequired();
        outbox.HasIndex(x => x.ProcessedAtUtc);

        var inbox = modelBuilder.Entity<InboxMessageEntity>();
        inbox.ToTable("inbox_messages", "messaging");
        inbox.HasKey(x => new { x.MessageId, x.Consumer });
        inbox.Property(x => x.Consumer).HasMaxLength(256).IsRequired();
        inbox.Property(x => x.ProcessedAtUtc).IsRequired();
    }
}
