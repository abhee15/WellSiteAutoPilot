using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence;

public sealed class WellSiteAutoPilotDbContext(DbContextOptions<WellSiteAutoPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<AssetTypeEntity> AssetTypes => Set<AssetTypeEntity>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<ExecutionEntity> Executions => Set<ExecutionEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var assetType = modelBuilder.Entity<AssetTypeEntity>();
        assetType.ToTable("asset_types", "assets");
        assetType.HasKey(x => x.Id);
        assetType.Property(x => x.Key).HasMaxLength(128).IsRequired();
        assetType.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        assetType.Property(x => x.SchemaVersion).IsRequired();
        assetType.Property(x => x.AttributeSchemaJson).IsRequired();
        assetType.Property(x => x.IsActive).IsRequired();
        assetType.Property(x => x.CreatedAtUtc).IsRequired();
        assetType.HasIndex(x => x.Key).IsUnique();

        var asset = modelBuilder.Entity<AssetEntity>();
        asset.ToTable("assets", "assets");
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Name).HasMaxLength(256).IsRequired();
        asset.Property(x => x.AttributeValuesJson).IsRequired();
        asset.Property(x => x.IsActive).IsRequired();
        asset.Property(x => x.CreatedAtUtc).IsRequired();
        asset.HasIndex(x => x.AssetTypeId);
        asset.HasIndex(x => x.ParentAssetId);
        asset.HasOne<AssetTypeEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        asset.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.ParentAssetId)
            .OnDelete(DeleteBehavior.Restrict);

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
        execution.Property(x => x.OutputJson);
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
