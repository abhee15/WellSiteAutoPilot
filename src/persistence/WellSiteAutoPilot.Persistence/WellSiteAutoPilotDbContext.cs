using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Messaging;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence;

public sealed class WellSiteAutoPilotDbContext(DbContextOptions<WellSiteAutoPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<AssetTypeEntity> AssetTypes => Set<AssetTypeEntity>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<ExecutionEntity> Executions => Set<ExecutionEntity>();
    public DbSet<ExecutionAssetScopeEntity> ExecutionAssetScopes => Set<ExecutionAssetScopeEntity>();
    public DbSet<ConfiguredLogicEntity> ConfiguredLogicDefinitions => Set<ConfiguredLogicEntity>();
    public DbSet<ConfiguredLogicRevisionEntity> ConfiguredLogicRevisions => Set<ConfiguredLogicRevisionEntity>();
    public DbSet<ConfiguredLogicAssetBindingEntity> ConfiguredLogicAssetBindings => Set<ConfiguredLogicAssetBindingEntity>();
    public DbSet<ConfiguredLogicDataBindingEntity> ConfiguredLogicDataBindings => Set<ConfiguredLogicDataBindingEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();
    public DbSet<LogicModuleCatalogEntity> LogicModules => Set<LogicModuleCatalogEntity>();
    public DbSet<RecommendationEntity> Recommendations => Set<RecommendationEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<UserAssetScopeEntity> UserAssetScopes => Set<UserAssetScopeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var auditEvent = modelBuilder.Entity<AuditEventEntity>();
        auditEvent.ToTable("audit_events", "audit");
        auditEvent.HasKey(x => x.Id);
        auditEvent.Property(x => x.OccurredAtUtc).IsRequired();
        auditEvent.Property(x => x.ActorIdentity).HasMaxLength(256).IsRequired();
        auditEvent.Property(x => x.Action).HasMaxLength(128).IsRequired();
        auditEvent.Property(x => x.TargetType).HasMaxLength(128).IsRequired();
        auditEvent.Property(x => x.TargetId).HasMaxLength(256);
        auditEvent.Property(x => x.CorrelationId).HasMaxLength(128);
        auditEvent.Property(x => x.DetailsJson).IsRequired();
        auditEvent.HasIndex(x => x.OccurredAtUtc);
        auditEvent.HasIndex(x => x.ActorUserId);
        auditEvent.HasIndex(x => x.Action);
        auditEvent.HasIndex(x => x.AssetId);

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

        var logicModule = modelBuilder.Entity<LogicModuleCatalogEntity>();
        logicModule.ToTable("logic_modules", "logic");
        logicModule.HasKey(x => x.Id);
        logicModule.Property(x => x.ModuleId).HasMaxLength(256).IsRequired();
        logicModule.Property(x => x.Version).HasMaxLength(64).IsRequired();
        logicModule.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        logicModule.Property(x => x.Publisher).HasMaxLength(256).IsRequired();
        logicModule.Property(x => x.Runtime).HasMaxLength(32).IsRequired();
        logicModule.Property(x => x.ExecutionProfile).HasMaxLength(64).IsRequired();
        logicModule.Property(x => x.ManifestJson).IsRequired();
        logicModule.Property(x => x.PackageSha256).HasMaxLength(64).IsRequired();
        logicModule.Property(x => x.TrustStatus).HasMaxLength(32).IsRequired();
        logicModule.Property(x => x.IsEnabled).IsRequired();
        logicModule.Property(x => x.InstalledAtUtc).IsRequired();
        logicModule.HasIndex(x => new { x.ModuleId, x.Version }).IsUnique();
        logicModule.HasIndex(x => x.TrustStatus);

        var execution = modelBuilder.Entity<ExecutionEntity>();
        execution.ToTable("executions", "operations");
        execution.HasKey(x => x.Id);
        execution.Property(x => x.ModuleId).HasMaxLength(256).IsRequired();
        execution.Property(x => x.ModuleVersion).HasMaxLength(64).IsRequired();
        execution.Property(x => x.AssetExternalId).HasMaxLength(512);
        execution.Property(x => x.Quantity).HasMaxLength(256);
        execution.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        execution.Property(x => x.Status).HasMaxLength(32).IsRequired();
        execution.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        execution.Property(x => x.RequestedAtUtc).IsRequired();
        execution.Property(x => x.RequestContractVersion).IsRequired();
        execution.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        execution.Property(x => x.RequestPayloadJson);
        execution.Property(x => x.ResultCode).HasMaxLength(128);
        execution.Property(x => x.FailureCode).HasMaxLength(128);
        execution.Property(x => x.OutputJson);
        execution.HasIndex(x => x.LogicInstanceId);
        execution.HasIndex(x => x.AssetId);
        execution.HasIndex(x => new { x.ConfigurationRevisionId, x.ScheduledForUtc })
            .IsUnique()
            .HasFilter("\"ScheduledForUtc\" IS NOT NULL");
        execution.HasIndex(x => new { x.Status, x.RequestedAtUtc });

        var executionAssetScope = modelBuilder.Entity<ExecutionAssetScopeEntity>();
        executionAssetScope.ToTable("execution_asset_scopes", "operations");
        executionAssetScope.HasKey(x => new { x.ExecutionId, x.AssetId });
        executionAssetScope.Property(x => x.LogicInstanceId).IsRequired();
        executionAssetScope.Property(x => x.AssetId).IsRequired();
        executionAssetScope.HasIndex(x => new { x.LogicInstanceId, x.AssetId });
        executionAssetScope.HasOne<ExecutionEntity>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        var recommendation = modelBuilder.Entity<RecommendationEntity>();
        recommendation.ToTable("recommendations", "operations");
        recommendation.HasKey(x => x.Id);
        recommendation.Property(x => x.IntentIndex).IsRequired();
        recommendation.Property(x => x.ModuleId).HasMaxLength(256).IsRequired();
        recommendation.Property(x => x.ModuleVersion).HasMaxLength(64).IsRequired();
        recommendation.Property(x => x.Code).HasMaxLength(128).IsRequired();
        recommendation.Property(x => x.Command).HasMaxLength(256).IsRequired();
        recommendation.Property(x => x.Quantity).HasMaxLength(256);
        recommendation.Property(x => x.Unit).HasMaxLength(64);
        recommendation.Property(x => x.ReasonCode).HasMaxLength(128).IsRequired();
        recommendation.Property(x => x.IntentJson).IsRequired();
        recommendation.Property(x => x.Status).HasMaxLength(32).IsRequired();
        recommendation.Property(x => x.CreatedAtUtc).IsRequired();
        recommendation.Property(x => x.DecisionBy).HasMaxLength(256);
        recommendation.Property(x => x.DecisionReason).HasMaxLength(1024);
        recommendation.HasIndex(x => new { x.ExecutionId, x.IntentIndex }).IsUnique();
        recommendation.HasIndex(x => x.ConfiguredLogicId);
        recommendation.HasIndex(x => x.ConfigurationRevisionId);
        recommendation.HasIndex(x => x.AssetId);
        recommendation.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        recommendation.HasOne<ExecutionEntity>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
        recommendation.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        recommendation.HasOne<ConfiguredLogicEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfiguredLogicId)
            .OnDelete(DeleteBehavior.Restrict);
        recommendation.HasOne<ConfiguredLogicRevisionEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConfigurationRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        var user = modelBuilder.Entity<UserEntity>();
        user.ToTable("users", "security");
        user.HasKey(x => x.Id);
        user.Property(x => x.IdentityKey).HasMaxLength(256).IsRequired();
        user.Property(x => x.IdentityName).HasMaxLength(256).IsRequired();
        user.Property(x => x.NormalizedIdentityName).HasMaxLength(256).IsRequired();
        user.Property(x => x.DisplayName).HasMaxLength(256);
        user.Property(x => x.IsActive).IsRequired();
        user.Property(x => x.CreatedAtUtc).IsRequired();
        user.Property(x => x.LastSeenAtUtc).IsRequired();
        user.HasIndex(x => x.IdentityKey).IsUnique();
        user.HasIndex(x => x.NormalizedIdentityName);

        var userRole = modelBuilder.Entity<UserRoleEntity>();
        userRole.ToTable("user_roles", "security");
        userRole.HasKey(x => new { x.UserId, x.Role });
        userRole.Property(x => x.Role).HasMaxLength(32).IsRequired();
        userRole.HasIndex(x => x.Role);
        userRole.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var userAssetScope = modelBuilder.Entity<UserAssetScopeEntity>();
        userAssetScope.ToTable("user_asset_scopes", "security");
        userAssetScope.HasKey(x => new { x.UserId, x.AssetId });
        userAssetScope.HasIndex(x => x.AssetId);
        userAssetScope.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        userAssetScope.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

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
