using System.Text.Json;
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
        if (manifest.RequiredAdapterKey is not ("foresite" or "cygnet") || targets.Length != 1) throw DirectSourceInvalid();
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
            sources.Length != 1 || sources[0].ConnectionId != connectionId || sources[0].ResourceKind != (manifest.RequiredAdapterKey == "cygnet" ? "cygnet.facility" : "foresite.well")) throw DirectSourceInvalid();
        if (revision.DataBindings.Any(binding => binding.AssetId != asset.Id || binding.ProviderId != connections[0].AdapterKey ||
            binding.ProviderAssetExternalId != sources[0].ExternalId)) throw DirectSourceInvalid();
    }

    private static WellSiteAutoPilotException DirectSourceInvalid() => new("CONFIGURED_LOGIC_DIRECT_SOURCE_INVALID", FailureKind.Validation,
        "Select one active imported well from an enabled compatible ForeSite Connection and use its stored source identity.");
}
