using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    private static readonly JsonSerializerOptions LifecycleManifestOptions = CreateLifecycleManifestOptions();

    internal async Task<ConfiguredLogicDefinition> LockForManualRequestAsync(Guid id, AccessSubject subject,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Manual request preparation requires a serializable transaction.");
        var definition = await LockConfigurationAsync(id, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
            definition.Revisions.SelectMany(DraftAssets).Distinct().Order().ToArray(), clock ?? TimeProvider.System, cancellationToken);
        return definition;
    }

    internal async Task<ConfiguredShadowExecutionCommand> PrepareLockedManualRequestAsync(ConfiguredLogicDefinition definition,
        Guid revisionId, AccessSubject subject, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Manual request preparation requires a serializable transaction.");
        var revision = definition.Revisions.SingleOrDefault(item => item.Id == revisionId);
        if (revision is null || definition.ActiveRevisionId != revisionId ||
            revision.Status != ConfiguredLogicRevisionStatus.Active || revision.Mode != ExecutionMode.Shadow) throw Conflict();
        var time = clock ?? TimeProvider.System;
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest,
            DraftAssets(revision), time, cancellationToken);
        await HoldRevisionDependenciesAsync(revision, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
            definition.Revisions.SelectMany(DraftAssets).Distinct().Order().ToArray(), time, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest,
            DraftAssets(revision), time, cancellationToken);
        return ConfiguredExecutionSnapshot.Create(definition, revision, ExecutionTriggerKind.Manual, null);
    }
    public Task SetRevisionValidatedAuthorizedAsync(ConfiguredLogicRevision reviewedRevision, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        ChangeRevisionAuthorizedAsync(reviewedRevision, null, false, subject, actor, cancellationToken);

    public Task ActivateRevisionAuthorizedAsync(ConfiguredLogicRevision reviewedRevision, Guid? expectedActiveRevisionId,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        ChangeRevisionAuthorizedAsync(reviewedRevision, expectedActiveRevisionId, true, subject, actor, cancellationToken);

    private Task ChangeRevisionAuthorizedAsync(ConfiguredLogicRevision reviewed, Guid? expectedActiveRevisionId,
        bool activate, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var expectedStatus = activate ? ConfiguredLogicRevisionStatus.Validated : ConfiguredLogicRevisionStatus.Draft;
        if (reviewed.Id == Guid.Empty || reviewed.ConfiguredLogicId == Guid.Empty ||
            reviewed.Mode != ExecutionMode.Shadow || reviewed.Status != expectedStatus) throw Conflict();
        return WriteConfigurationAuthorizedAsync(subject, actor, async () =>
        {
            var definition = await LockConfigurationAsync(reviewed.ConfiguredLogicId, cancellationToken);
            var targets = definition.Revisions.SelectMany(DraftAssets).Distinct().Order().ToArray();
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            var current = definition.Revisions.SingleOrDefault(item => item.Id == reviewed.Id);
            if (current is null || RevisionSnapshot(current) != RevisionSnapshot(reviewed) ||
                (activate && definition.ActiveRevisionId != expectedActiveRevisionId)) throw Conflict();

            await HoldRevisionDependenciesAsync(current, cancellationToken);
            // Dependency locks can wait. Check proof freshness again after all
            // mutable prerequisites have been retained through the commit.
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            int changed;
            if (activate)
            {
                await dbContext.ConfiguredLogicRevisions
                    .Where(item => item.ConfiguredLogicId == definition.Id && item.Status == nameof(ConfiguredLogicRevisionStatus.Active))
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, nameof(ConfiguredLogicRevisionStatus.Superseded)), cancellationToken);
                changed = await dbContext.ConfiguredLogicRevisions
                    .Where(item => item.Id == current.Id && item.Status == nameof(ConfiguredLogicRevisionStatus.Validated))
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, nameof(ConfiguredLogicRevisionStatus.Active))
                        .SetProperty(item => item.ActivatedAtUtc, now), cancellationToken);
                await dbContext.ConfiguredLogicDefinitions.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.ActiveRevisionId, current.Id), cancellationToken);
            }
            else
            {
                changed = await dbContext.ConfiguredLogicRevisions
                    .Where(item => item.Id == current.Id && item.Status == nameof(ConfiguredLogicRevisionStatus.Draft))
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, nameof(ConfiguredLogicRevisionStatus.Validated))
                        .SetProperty(item => item.ValidatedAtUtc, now), cancellationToken);
            }
            if (changed != 1) throw Conflict();
            Audit(current, activate ? "CONFIGURED_LOGIC_REVISION_ACTIVATED" : "CONFIGURED_LOGIC_REVISION_VALIDATED", actor, now);
        }, cancellationToken);
    }

    private async Task HoldRevisionDependenciesAsync(ConfiguredLogicRevision revision, CancellationToken cancellationToken)
    {
        var modules = await dbContext.LogicModules.FromSqlInterpolated($"""
            SELECT m.* FROM logic.logic_modules AS m
            WHERE m."ModuleId" = {revision.ModuleId} AND m."Version" = {revision.ModuleVersion} FOR SHARE OF m
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (modules.Length != 1 || !modules[0].IsEnabled ||
            NormalizeManifest(modules[0].ManifestJson) != NormalizeManifest(revision.ModuleManifestJson)) throw DependencyConflict();
        var manifest = JsonSerializer.Deserialize<LogicModuleManifest>(modules[0].ManifestJson, LifecycleManifestOptions) ?? throw DependencyConflict();
        LogicModuleManifestValidator.Validate(manifest);
        if (revision.View is { } source)
        {
            var connections = await dbContext.Connections.FromSqlInterpolated($"""
                SELECT c.* FROM integration.connections AS c WHERE c."Id" = {source.ConnectionId} FOR SHARE OF c
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (connections.Length != 1 || !connections[0].IsEnabled || (manifest.RequiredAdapterKey is { } adapter &&
                !string.Equals(adapter, connections[0].AdapterKey, StringComparison.OrdinalIgnoreCase))) throw DependencyConflict();
        }
        else if (manifest.RequiredAdapterKey is not null) throw DependencyConflict();

        if (manifest.ModuleId != revision.ModuleId || manifest.Version != revision.ModuleVersion) throw DependencyConflict();

        // The grant fence already retains these Asset rows. Retain their type
        // records too: a type can be retired or renamed after service validation.
        var targets = DraftAssets(revision);
        var assets = await dbContext.Assets.AsNoTracking().Where(item => targets.Contains(item.Id)).ToArrayAsync(cancellationToken);
        var typeIds = assets.Select(item => item.AssetTypeId).Distinct().Order().ToArray();
        var types = await dbContext.AssetTypes.FromSqlInterpolated($"""
            SELECT t.* FROM assets.asset_types AS t WHERE t."Id" = ANY({typeIds}) ORDER BY t."Id" FOR SHARE OF t
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var assetsById = assets.ToDictionary(asset => asset.Id);
        foreach (var binding in revision.AssetBindings)
        {
            var requirement = manifest.AssetRequirements.SingleOrDefault(item => string.Equals(item.Role, binding.Role, StringComparison.OrdinalIgnoreCase));
            var asset = assetsById.GetValueOrDefault(binding.AssetId);
            if (requirement is null || asset is null || !asset.IsActive ||
                (revision.View is { } view && asset.OwnerConnectionId != view.ConnectionId)) throw DependencyConflict();
            if (!string.IsNullOrWhiteSpace(requirement.RequiredAssetTypeKey) &&
                !types.Any(item => item.Id == asset.AssetTypeId && item.IsActive &&
                    string.Equals(item.Key, requirement.RequiredAssetTypeKey, StringComparison.OrdinalIgnoreCase))) throw DependencyConflict();
        }
    }

    private static string RevisionSnapshot(ConfiguredLogicRevision revision) => JsonSerializer.Serialize(revision with
    {
        AssetBindings = revision.AssetBindings.OrderBy(item => item.Role, StringComparer.Ordinal).ThenBy(item => item.AssetId).ToArray(),
        DataBindings = revision.DataBindings.OrderBy(item => item.RequirementId, StringComparer.Ordinal).ThenBy(item => item.AssetId).ToArray()
    });

    private static string NormalizeManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static WellSiteAutoPilotException DependencyConflict() => new("CONFIGURED_LOGIC_REVISION_CONFLICT", FailureKind.Conflict,
        "Configuration dependencies changed. Refresh and validate before retrying.");

    private static JsonSerializerOptions CreateLifecycleManifestOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
