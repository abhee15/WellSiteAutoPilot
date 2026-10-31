using System.Text.Json;
using WellSiteAutoPilot.Persistence.Connections;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    // Called only after the existing paired read/manage grant fence holds all
    // revision targets. Draft, validation, activation and manual dispatch reuse it.
    private async Task RequireDirectSourceSnapshotAsync(ConfiguredLogicRevision revision, CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<LogicModuleManifest>(revision.ModuleManifestJson, LifecycleManifestOptions)
            ?? throw DependencyConflict();
        if (manifest.RequiredAdapterKey is null) return;
        var targets = DraftAssets(revision);
        var policy = SavedConnectionReadPolicies.Find(manifest.RequiredAdapterKey);
        if (policy is null || targets.Length != 1) throw DirectSourceInvalid();
        var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == targets[0], cancellationToken);
        if (asset is null || !asset.IsActive || asset.OwnerConnectionId is not { } connectionId ||
            await dbContext.Assets.AnyAsync(a => a.IsActive && a.ParentAssetId == asset.Id, cancellationToken)) throw DirectSourceInvalid();
        var connections = await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {connectionId} FOR SHARE OF c
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var sources = await dbContext.SourceBindings.FromSqlInterpolated($"""
            SELECT b.* FROM integration.source_bindings AS b WHERE b."AssetId" = {asset.Id} FOR SHARE OF b
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (connections.Length != 1 || !connections[0].IsEnabled || connections[0].AdapterKey != manifest.RequiredAdapterKey ||
            sources.Length != 1 || sources[0].ConnectionId != connectionId || sources[0].ResourceKind != policy.ResourceKind) throw DirectSourceInvalid();
        var settings = JsonSerializer.Deserialize<Dictionary<string, string?>>(connections[0].SettingsJson, LifecycleManifestOptions)
            ?? throw DirectSourceInvalid();
        if (!policy.IsSource(settings, sources[0].ExternalId)) throw DirectSourceInvalid();
        foreach (var binding in revision.DataBindings)
        {
            var requirement = manifest.DataRequirements.SingleOrDefault(r => r.Id == binding.RequirementId);
            if (requirement is null || !policy.CanRead(settings, binding.ProviderAssetExternalId, requirement.Quantity,
                requirement.Access, requirement.RequiredCapability, requirement.ValueSchema?.SchemaId)) throw DirectSourceInvalid();
        }
        if (revision.DataBindings.Any(binding => binding.AssetId != asset.Id || binding.ProviderId != connections[0].AdapterKey ||
            binding.ProviderAssetExternalId != sources[0].ExternalId)) throw DirectSourceInvalid();
    }

    private static WellSiteAutoPilotException DirectSourceInvalid() => new("CONFIGURED_LOGIC_DIRECT_SOURCE_INVALID", FailureKind.Validation,
        "Select one active imported asset from an enabled compatible Connection and use its stored source identity.");
}
