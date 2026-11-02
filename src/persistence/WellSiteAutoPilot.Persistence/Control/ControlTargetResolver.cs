using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Persistence.Control;

/// <summary>
/// Finds where a command for an asset would go: the asset's own connection, the provider-side id, and the connection's
/// command catalog entry. Every doubt is a refusal with a stated code, never a guess. A system read for the control
/// gate; it does not check what a person may see, because no person asks it.
/// </summary>
public sealed class ControlTargetResolver(WellSiteAutoPilotDbContext dbContext, IConnectionCommandCatalogRepository catalogs)
    : IControlTargetResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ControlTargetResolution> ResolveAsync(
        Guid assetId, string command, IReadOnlyCollection<ConfiguredLogicDataBinding> dataBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataBindings);
        if (assetId == Guid.Empty || string.IsNullOrWhiteSpace(command))
            return ControlTargetResolution.Refused(GateCodes.TargetNoConnection);

        var asset = await dbContext.Assets.AsNoTracking()
            .Where(item => item.Id == assetId).Select(item => new { item.IsActive, item.OwnerConnectionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (asset is not { IsActive: true, OwnerConnectionId: { } connectionId })
            return ControlTargetResolution.Refused(GateCodes.TargetNoConnection);

        var connection = await dbContext.Connections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == connectionId, cancellationToken);
        if (connection is not { IsEnabled: true })
            return ControlTargetResolution.Refused(GateCodes.TargetConnectionDisabled);

        var imported = await dbContext.SourceBindings.AsNoTracking()
            .Where(item => item.AssetId == assetId && item.ConnectionId == connectionId)
            .Select(item => item.ExternalId).ToArrayAsync(cancellationToken);
        var configured = dataBindings
            .Where(item => item.AssetId == assetId && string.Equals(item.ProviderId, connection.AdapterKey, StringComparison.Ordinal))
            .Select(item => item.ProviderAssetExternalId).ToArray();
        var (externalId, refusal) = ControlTargetRules.PickExternalId(imported, configured);
        if (externalId is null) return ControlTargetResolution.Refused(refusal ?? GateCodes.TargetNotBound);

        var catalog = await catalogs.FindAsync(connectionId, cancellationToken);
        var entry = ControlTargetRules.Find(catalog, command);
        if (entry is null) return ControlTargetResolution.Refused(GateCodes.CommandNotInCatalog);

        Dictionary<string, string?>? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Dictionary<string, string?>>(connection.SettingsJson, Json);
        }
        catch (JsonException)
        {
            settings = null;
        }

        // Connection settings that cannot be read mean the connection cannot be trusted to address anything.
        return settings is null
            ? ControlTargetResolution.Refused(GateCodes.TargetConnectionDisabled)
            : ControlTargetResolution.Resolved(new ControlTarget(
                connection.Id, connection.Revision, connection.AdapterKey, externalId, settings, entry, catalog!.Version));
    }
}
