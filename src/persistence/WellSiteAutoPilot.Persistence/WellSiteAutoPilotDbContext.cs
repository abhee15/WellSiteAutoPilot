using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Assets.Views;
using WellSiteAutoPilot.Persistence.Connections;
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
    public DbSet<ConnectionEntity> Connections => Set<ConnectionEntity>();
    public DbSet<AssetViewMemberEntity> AssetViewMembers => Set<AssetViewMemberEntity>();
    public DbSet<AssetViewEntity> AssetViews => Set<AssetViewEntity>();
    public DbSet<SourceCheckEntity> SourceChecks => Set<SourceCheckEntity>();
    public DbSet<SourceChangeEntity> SourceChanges => Set<SourceChangeEntity>();
    public DbSet<SourceChangeImpactEntity> SourceChangeImpacts => Set<SourceChangeImpactEntity>();
    public DbSet<ImportHierarchyEntity> ImportHierarchies => Set<ImportHierarchyEntity>();
    public DbSet<ImportedGroupEntity> ImportedGroups => Set<ImportedGroupEntity>();
    public DbSet<SourceBindingEntity> SourceBindings => Set<SourceBindingEntity>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<ExecutionEntity> Executions => Set<ExecutionEntity>();
    public DbSet<ExecutionAssetScopeEntity> ExecutionAssetScopes => Set<ExecutionAssetScopeEntity>();
    public DbSet<ConfiguredLogicEntity> ConfiguredLogicDefinitions => Set<ConfiguredLogicEntity>();
    public DbSet<ConfiguredLogicRevisionEntity> ConfiguredLogicRevisions => Set<ConfiguredLogicRevisionEntity>();
    public DbSet<ConfiguredLogicAssetBindingEntity> ConfiguredLogicAssetBindings => Set<ConfiguredLogicAssetBindingEntity>();
    public DbSet<ConfiguredLogicDataBindingEntity> ConfiguredLogicDataBindings => Set<ConfiguredLogicDataBindingEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();
    public DbSet<LogicModulePackageEntity> LogicModulePackages => Set<LogicModulePackageEntity>();
    public DbSet<LogicModuleCatalogEntity> LogicModules => Set<LogicModuleCatalogEntity>();
    public DbSet<RecommendationEntity> Recommendations => Set<RecommendationEntity>();
    public DbSet<WellSiteAutoPilot.Persistence.Control.AutonomyPolicyEntity> AutonomyPolicies => Set<WellSiteAutoPilot.Persistence.Control.AutonomyPolicyEntity>();
    public DbSet<WellSiteAutoPilot.Persistence.Authoring.AuthoringDraftEntity> AuthoringDrafts => Set<WellSiteAutoPilot.Persistence.Authoring.AuthoringDraftEntity>();
    public DbSet<WellSiteAutoPilot.Persistence.Authoring.AuthoringPackageEntity> AuthoringPackages => Set<WellSiteAutoPilot.Persistence.Authoring.AuthoringPackageEntity>();
    public DbSet<WellSiteAutoPilot.Persistence.Control.ControlActionEntity> ControlActions => Set<WellSiteAutoPilot.Persistence.Control.ControlActionEntity>();
    public DbSet<WellSiteAutoPilot.Persistence.Control.ControlKillSwitchEntity> ControlKillSwitches => Set<WellSiteAutoPilot.Persistence.Control.ControlKillSwitchEntity>();
    public DbSet<RoleDefinitionEntity> RoleDefinitions => Set<RoleDefinitionEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<AccessGrantEntity> AccessGrants => Set<AccessGrantEntity>();
    public DbSet<AccessGrantAssetEntity> AccessGrantAssets => Set<AccessGrantAssetEntity>();
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
        assetType.Property(x => x.HiddenNote).HasMaxLength(200);
        assetType.Property(x => x.HiddenBy).HasMaxLength(256);
        assetType.HasIndex(x => x.Key).IsUnique();

        var connection = modelBuilder.Entity<ConnectionEntity>();
        connection.ToTable("connections", "integration");
        connection.HasKey(x => x.Id);
        connection.Property(x => x.Name).HasMaxLength(100).IsRequired();
        connection.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        connection.Property(x => x.AdapterKey).HasMaxLength(64).IsRequired();
        connection.Property(x => x.SettingsVersion).IsRequired();
        connection.Property(x => x.SettingsJson).IsRequired();
        connection.Property(x => x.IsEnabled).IsRequired();
        connection.Property(x => x.Revision).IsRequired();
        connection.Property(x => x.CreatedAtUtc).IsRequired();
        connection.Property(x => x.UpdatedAtUtc).IsRequired();
        connection.Property(x => x.LastTestOutcome).HasMaxLength(32);
        connection.Property(x => x.LastTestMessage).HasMaxLength(500);
        connection.Property(x => x.LastTestActingAs).HasMaxLength(256);
        connection.Property(x => x.LastTestSource).HasMaxLength(16);
        connection.Property(x => x.ConsecutiveFailureCount).IsRequired().HasDefaultValue(0);
        connection.HasIndex(x => x.NormalizedName).IsUnique();

        var assetView = modelBuilder.Entity<AssetViewEntity>();
        assetView.ToTable("asset_views", "assets");
        assetView.HasKey(x => x.Id);
        assetView.Property(x => x.Name).HasMaxLength(48).IsRequired();
        assetView.Property(x => x.NormalizedName).HasMaxLength(48).IsRequired();
        assetView.Property(x => x.Visibility).IsRequired().HasDefaultValue(0);
        assetView.Property(x => x.DefinitionJson).IsRequired();
        assetView.Property(x => x.Revision).IsRequired();
        assetView.Property(x => x.CreatedAtUtc).IsRequired();
        assetView.Property(x => x.UpdatedAtUtc).IsRequired();
        assetView.HasIndex(x => new { x.OwnerUserId, x.NormalizedName }).IsUnique().HasFilter("\"ConnectionId\" IS NULL");
        assetView.HasIndex(x => new { x.ConnectionId, x.NormalizedName }).IsUnique().HasFilter("\"ConnectionId\" IS NOT NULL");
        assetView.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);

        var viewMember = modelBuilder.Entity<AssetViewMemberEntity>();
        viewMember.ToTable("asset_view_members", "assets");
        viewMember.HasKey(x => new { x.ViewId, x.AssetId });
        viewMember.HasOne<AssetViewEntity>().WithMany().HasForeignKey(x => x.ViewId).OnDelete(DeleteBehavior.Cascade);
        viewMember.HasOne<AssetEntity>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);

        var check = modelBuilder.Entity<SourceCheckEntity>();
        check.ToTable("source_checks", "integration");
        check.HasKey(x => x.Id);
        check.Property(x => x.AcceptedBindingsFingerprint).HasMaxLength(64).IsRequired();
        check.Property(x => x.Status).HasMaxLength(32).IsRequired();
        check.Property(x => x.ActingAs).HasMaxLength(256);
        check.Property(x => x.WarningsJson).IsRequired();
        check.HasIndex(x => new { x.ConnectionId, x.Revision }).IsUnique();
        check.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        var change = modelBuilder.Entity<SourceChangeEntity>();
        change.ToTable("source_changes", "integration");
        change.HasKey(x => x.Id);
        change.Property(x => x.ResourceKind).HasMaxLength(128).IsRequired();
        change.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        change.Property(x => x.SourceName).HasMaxLength(256).IsRequired();
        change.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        change.Property(x => x.Status).HasMaxLength(32).IsRequired();
        change.Property(x => x.EvidenceJson).IsRequired();
        change.HasIndex(x => new { x.ConnectionId, x.ResourceKind, x.ExternalId, x.Fingerprint }).IsUnique();
        change.HasIndex(x => new { x.ConnectionId, x.LastSeenAtUtc });
        change.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        change.HasOne<AssetEntity>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        change.HasOne<SourceCheckEntity>().WithMany().HasForeignKey(x => x.FirstCheckId).OnDelete(DeleteBehavior.Restrict);
        change.HasOne<SourceCheckEntity>().WithMany().HasForeignKey(x => x.LastCheckId).OnDelete(DeleteBehavior.Restrict);
        var impact = modelBuilder.Entity<SourceChangeImpactEntity>();
        impact.ToTable("source_change_impacts", "integration");
        impact.HasKey(x => new { x.ChangeId, x.RevisionId, x.Kind, x.Reference });
        impact.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        impact.Property(x => x.Reference).HasMaxLength(128).IsRequired();
        impact.HasOne<SourceChangeEntity>().WithMany().HasForeignKey(x => x.ChangeId).OnDelete(DeleteBehavior.Restrict);
        impact.HasOne<ConfiguredLogicRevisionEntity>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);

        var hierarchy = modelBuilder.Entity<ImportHierarchyEntity>();
        hierarchy.ToTable("import_hierarchies", "integration");
        hierarchy.HasKey(x => x.ConnectionId);
        hierarchy.Property(x => x.LevelsJson).IsRequired();
        hierarchy.Property(x => x.FieldsJson).IsRequired();
        hierarchy.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        var importedGroup = modelBuilder.Entity<ImportedGroupEntity>();
        importedGroup.ToTable("imported_groups", "integration");
        importedGroup.HasKey(x => new { x.ConnectionId, x.GroupKey });
        importedGroup.Property(x => x.GroupKey).HasMaxLength(64).IsRequired();
        importedGroup.Property(x => x.SourceLabel).HasMaxLength(256).IsRequired();
        importedGroup.HasIndex(x => x.AssetId).IsUnique();
        importedGroup.HasOne<ImportHierarchyEntity>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        importedGroup.HasOne<AssetEntity>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);

        var sourceBinding = modelBuilder.Entity<SourceBindingEntity>();
        sourceBinding.ToTable("source_bindings", "integration");
        sourceBinding.HasKey(x => x.Id);
        sourceBinding.Property(x => x.ResourceKind).HasMaxLength(128).IsRequired();
        sourceBinding.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        sourceBinding.Property(x => x.SnapshotJson).IsRequired();
        sourceBinding.Property(x => x.CreatedAtUtc).IsRequired();
        sourceBinding.HasIndex(x => new { x.ConnectionId, x.ResourceKind, x.ExternalId }).IsUnique();
        sourceBinding.HasIndex(x => x.AssetId);
        sourceBinding.HasOne<ConnectionEntity>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
        sourceBinding.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        var asset = modelBuilder.Entity<AssetEntity>();
        asset.ToTable("assets", "assets");
        asset.HasKey(x => x.Id);
        asset.HasIndex(x => x.OwnerConnectionId);
        asset.HasOne<ConnectionEntity>()
            .WithMany()
            .HasForeignKey(x => x.OwnerConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
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
        configuredLogicRevision.Property(x => x.ViewRole).HasMaxLength(128);
        configuredLogicRevision.HasOne<AssetViewEntity>().WithMany().HasForeignKey(x => x.ViewId).OnDelete(DeleteBehavior.Restrict);
        configuredLogicRevision.ToTable(table => table.HasCheckConstraint("CK_configured_logic_view_snapshot",
            "(\"ViewId\" IS NULL AND \"ViewRevision\" IS NULL AND \"ViewConnectionId\" IS NULL AND \"ViewRole\" IS NULL) OR " +
            "(\"ViewId\" IS NOT NULL AND \"ViewRevision\" IS NOT NULL AND \"ViewRevision\" > 0 AND \"ViewConnectionId\" IS NOT NULL AND \"ViewRole\" IS NOT NULL)"));
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

        var modulePackage = modelBuilder.Entity<LogicModulePackageEntity>();
        modulePackage.ToTable("module_packages", "logic", table => table.HasCheckConstraint(
            "CK_module_packages_content_size", "octet_length(\"Content\") BETWEEN 1 AND 16777216"));
        modulePackage.HasKey(x => x.Sha256);
        modulePackage.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        modulePackage.Property(x => x.Content).HasColumnType("bytea").IsRequired();
        modulePackage.Property(x => x.UploadedAtUtc).IsRequired();
        modulePackage.Property(x => x.UploadedByUserId).IsRequired();

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
        execution.Property(x => x.ReadAuthorityJson);
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
        WellSiteAutoPilot.Persistence.Control.ControlModelConfiguration.Configure(modelBuilder);
        WellSiteAutoPilot.Persistence.Authoring.AuthoringModelConfiguration.Configure(modelBuilder);
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

        var roleDefinition = modelBuilder.Entity<RoleDefinitionEntity>();
        roleDefinition.ToTable("role_definitions", "security");
        roleDefinition.HasKey(item => item.Id);
        roleDefinition.Property(item => item.Key).HasMaxLength(128).IsRequired();
        roleDefinition.Property(item => item.Name).HasMaxLength(80).IsRequired();
        roleDefinition.Property(item => item.NormalizedName).HasMaxLength(80).IsRequired();
        roleDefinition.Property(item => item.Description).HasMaxLength(500).IsRequired();
        roleDefinition.Property(item => item.IsSystem).IsRequired();
        roleDefinition.Property(item => item.IsActive).IsRequired();
        roleDefinition.Property(item => item.Revision).IsRequired();
        roleDefinition.Property(item => item.CreatedAtUtc).IsRequired();
        roleDefinition.Property(item => item.UpdatedAtUtc).IsRequired();
        roleDefinition.HasIndex(item => item.Key).IsUnique();
        roleDefinition.HasIndex(item => item.NormalizedName).IsUnique();

        var rolePermission = modelBuilder.Entity<RolePermissionEntity>();
        rolePermission.ToTable("role_permissions", "security");
        rolePermission.HasKey(item => new { item.RoleId, item.Permission });
        rolePermission.Property(item => item.Permission).HasMaxLength(128).IsRequired();
        rolePermission.HasOne<RoleDefinitionEntity>()
            .WithMany()
            .HasForeignKey(item => item.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        var grant = modelBuilder.Entity<AccessGrantEntity>();
        grant.ToTable("access_grants", "security", table =>
        {
            table.HasCheckConstraint("CK_access_grants_subject",
                "\"SubjectKind\" = 'User' AND \"UserId\" IS NOT NULL AND \"GroupId\" IS NULL OR " +
                "\"SubjectKind\" = 'Group' AND \"UserId\" IS NULL AND \"GroupId\" IS NOT NULL AND \"DirectoryId\" IS NOT NULL");
            table.HasCheckConstraint("CK_access_grants_scope_kind",
                "\"ScopeKind\" IN ('ExactAssets', 'DeploymentWide', 'AllAssets')");
            table.HasCheckConstraint("CK_access_grants_revision", "\"Revision\" >= 1");
        });
        grant.HasKey(item => item.Id);
        grant.Property(item => item.SubjectKind).HasMaxLength(32).IsRequired();
        grant.Property(item => item.GroupId).HasMaxLength(256).UseCollation("C");
        grant.Property(item => item.DirectoryId).HasMaxLength(64).UseCollation("C").UsePropertyAccessMode(PropertyAccessMode.Property);
        grant.Property(item => item.GroupDisplayName).HasMaxLength(256);
        grant.Property(item => item.GroupDomain).HasMaxLength(256);
        grant.Property(item => item.ScopeKind).HasMaxLength(32).IsRequired();
        grant.Property(item => item.IsActive).IsRequired();
        grant.Property(item => item.Revision).IsRequired();
        grant.Property(item => item.CreatedAtUtc).IsRequired();
        grant.Property(item => item.UpdatedAtUtc).IsRequired();
        grant.HasIndex(item => item.UserId);
        grant.HasIndex(item => item.GroupId);
        grant.HasIndex(item => item.RoleId);
        grant.HasIndex(item => new { item.UserId, item.RoleId, item.ScopeKind })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"SubjectKind\" = 'User'");
        grant.HasIndex(item => new { item.DirectoryId, item.GroupId, item.RoleId, item.ScopeKind })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"SubjectKind\" = 'Group'");
        grant.HasOne<UserEntity>().WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        grant.HasOne<RoleDefinitionEntity>().WithMany()
            .HasForeignKey(item => item.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        var grantAsset = modelBuilder.Entity<AccessGrantAssetEntity>();
        grantAsset.ToTable("assignment_asset_scopes", "security");
        grantAsset.HasKey(item => new { item.GrantId, item.AssetId });
        grantAsset.HasIndex(item => item.AssetId);
        grantAsset.HasOne<AccessGrantEntity>().WithMany()
            .HasForeignKey(item => item.GrantId)
            .OnDelete(DeleteBehavior.Cascade);
        grantAsset.HasOne<AssetEntity>().WithMany()
            .HasForeignKey(item => item.AssetId)
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
        outbox.Property(x => x.LastFailureCode).HasMaxLength(128);
        outbox.HasIndex(x => new { x.Type, x.ProcessedAtUtc, x.NextAttemptAtUtc });
        outbox.HasIndex(x => x.ProcessedAtUtc);

        var inbox = modelBuilder.Entity<InboxMessageEntity>();
        inbox.ToTable("inbox_messages", "messaging");
        inbox.HasKey(x => new { x.MessageId, x.Consumer });
        inbox.Property(x => x.Consumer).HasMaxLength(256).IsRequired();
        inbox.Property(x => x.ProcessedAtUtc).IsRequired();
    }
}
