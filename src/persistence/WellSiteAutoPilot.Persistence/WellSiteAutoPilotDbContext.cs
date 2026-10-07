using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence;

public sealed class WellSiteAutoPilotDbContext(DbContextOptions<WellSiteAutoPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<AssetTypeEntity> AssetTypes => Set<AssetTypeEntity>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<ExecutionEntity> Executions => Set<ExecutionEntity>();
    public DbSet<ConfiguredLogicEntity> ConfiguredLogicDefinitions => Set<ConfiguredLogicEntity>();
    public DbSet<ConfiguredLogicRevisionEntity> ConfiguredLogicRevisions => Set<ConfiguredLogicRevisionEntity>();
    public DbSet<ConfiguredLogicAssetBindingEntity> ConfiguredLogicAssetBindings => Set<ConfiguredLogicAssetBindingEntity>();
    public DbSet<ConfiguredLogicDataBindingEntity> ConfiguredLogicDataBindings => Set<ConfiguredLogicDataBindingEntity>();
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

        var configuredLogic = modelBuilder.Entity<ConfiguredLogicEntity>();
        configuredLogic.ToTable("configured_logic", "logic");
        configuredLogic.HasKey(x => x.Id);
        configuredLogic.Property(x => x.Name).HasMaxLength(256).IsRequired();
        configuredLogic.Property(x => x.CreatedAtUtc).IsRequired();
        configuredLogic.HasIndex(x => x.ActiveRevisionId);

        var configuredLogicRevision = modelBuilder.Entity<ConfiguredLogicRevisionEntity>();
        configuredLogicRevision.ToTable("configured_logic_revisions", "logic");
        configuredLogicRevision.HasKey(x => x.Id);
        configuredLogicRevision.Property(x => x.RevisionNumber).IsRequired();
        configuredLogicRevision.Property(x => x.ModuleId).HasMaxLength(256).IsRequired();
        configuredLogicRevision.Property(x => x.ModuleVersion).HasMaxLength(64).IsRequired();
        configuredLogicRevision.Property(x => x.ModuleManifestJson).IsRequired();
        configuredLogicRevision.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        configuredLogicRevision.Property(x => x.ParametersJson).IsRequired();
        configuredLogicRevision.Property(x => x.ScheduleJson);
        configuredLogicRevision.Property(x => x.Status).HasMaxLength(32).IsRequired();
        configuredLogicRevision.Property(x => x.CreatedAtUtc).IsRequired();
        configuredLogicRevision.HasIndex(x => new { x.ConfiguredLogicId, x.RevisionNumber }).IsUnique();
        configuredLogicRevision.HasIndex(x => x.Status);
        configuredLogicRevision.HasOne<ConfiguredLogicEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfiguredLogicId)
            .OnDelete(DeleteBehavior.Cascade);

        var configuredLogicBinding = modelBuilder.Entity<ConfiguredLogicAssetBindingEntity>();
        configuredLogicBinding.ToTable("configured_logic_asset_bindings", "logic");
        configuredLogicBinding.HasKey(x => new { x.RevisionId, x.Role, x.AssetId });
        configuredLogicBinding.Property(x => x.Role).HasMaxLength(128).IsRequired();
        configuredLogicBinding.Property(x => x.ParameterOverridesJson).IsRequired();
        configuredLogicBinding.HasIndex(x => x.AssetId);
        configuredLogicBinding.HasOne<ConfiguredLogicRevisionEntity>()
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        configuredLogicBinding.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        var configuredLogicDataBinding = modelBuilder.Entity<ConfiguredLogicDataBindingEntity>();
        configuredLogicDataBinding.ToTable("configured_logic_data_bindings", "logic");
        configuredLogicDataBinding.HasKey(x => new { x.RevisionId, x.RequirementId, x.AssetId });
        configuredLogicDataBinding.Property(x => x.RequirementId).HasMaxLength(128).IsRequired();
        configuredLogicDataBinding.Property(x => x.ProviderId).HasMaxLength(256).IsRequired();
        configuredLogicDataBinding.Property(x => x.ProviderAssetExternalId).HasMaxLength(512).IsRequired();
        configuredLogicDataBinding.Property(x => x.ProviderMappingJson).IsRequired();
        configuredLogicDataBinding.HasIndex(x => x.AssetId);
        configuredLogicDataBinding.HasOne<ConfiguredLogicRevisionEntity>()
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        configuredLogicDataBinding.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
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
