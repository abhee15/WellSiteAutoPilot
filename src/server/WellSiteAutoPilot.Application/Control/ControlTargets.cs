using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>
/// Where one command goes and what the site allows for it, resolved from the asset's own connection and the
/// connection's command catalog. A module never supplies any of it. Server/Gateway boundary only: it carries the
/// connection settings, so it must not be returned from an API or stored on a recommendation.
/// </summary>
public sealed record ControlTarget(
    Guid ConnectionId,
    int ConnectionRevision,
    string AdapterKey,
    string ExternalId,
    IReadOnlyDictionary<string, string?> Settings,
    ConnectionCommandCatalogEntry Entry,
    int CatalogVersion);

/// <summary>Exactly one of the two is set: the target, or the gate code that says why there is none.</summary>
public sealed record ControlTargetResolution(ControlTarget? Target, string? RefusalCode)
{
    public static ControlTargetResolution Refused(string code) => new(null, code);

    public static ControlTargetResolution Resolved(ControlTarget target) => new(target, null);
}

public interface IControlTargetResolver
{
    /// <summary>
    /// Resolves where <paramref name="command"/> for <paramref name="assetId"/> would be sent. A system read used by the
    /// control gate itself; never expose it to a request.
    /// </summary>
    /// <param name="dataBindings">
    /// The live revision's engineer-configured data bindings. They name the provider-side id of an asset that has no
    /// imported source binding (the simulator, for one). They never choose the connection or the command.
    /// </param>
    Task<ControlTargetResolution> ResolveAsync(
        Guid assetId, string command, IReadOnlyCollection<ConfiguredLogicDataBinding> dataBindings,
        CancellationToken cancellationToken = default);
}

/// <summary>Pure rules for turning what is stored into one target, so they can be proven without a database.</summary>
public static class ControlTargetRules
{
    /// <summary>
    /// The provider-side id for an asset. An imported source binding and an engineer's data binding that both exist
    /// must agree, and more than one distinct id is never guessed between.
    /// </summary>
    public static (string? ExternalId, string? RefusalCode) PickExternalId(
        IReadOnlyCollection<string> sourceBindingIds, IReadOnlyCollection<string> dataBindingIds)
    {
        var imported = sourceBindingIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var configured = dataBindingIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (imported.Length > 1 || configured.Length > 1) return (null, GateCodes.TargetAmbiguous);
        if (imported.Length == 1 && configured.Length == 1)
            return string.Equals(imported[0], configured[0], StringComparison.Ordinal)
                ? (imported[0], null)
                : (null, GateCodes.TargetAmbiguous);
        if (imported.Length == 1) return (imported[0], null);
        if (configured.Length == 1) return (configured[0], null);
        return (null, GateCodes.TargetNotBound);
    }

    /// <summary>
    /// What the site's catalog says about one value: the entry must exist, be switched on, and hold the value inside
    /// its hard limits. Null means the catalog allows it. The step limit needs the value now on the equipment, which the
    /// gate does not read, so it is checked where a current value is available.
    /// </summary>
    public static string? CheckCatalog(ConnectionCommandCatalogEntry? entry, decimal value)
    {
        if (entry is null) return GateCodes.CommandNotInCatalog;
        if (!entry.Enabled) return GateCodes.CommandNotEnabled;
        if (entry.Minimum is not { } minimum || entry.Maximum is not { } maximum || value < minimum || value > maximum)
            return GateCodes.CatalogOutOfBounds;
        return null;
    }

    /// <summary>Catalog names are unique ignoring case, so a policy's spelling still finds the entry.</summary>
    public static ConnectionCommandCatalogEntry? Find(ConnectionCommandCatalog? catalog, string command) =>
        catalog?.Entries.FirstOrDefault(entry => string.Equals(entry.Command, command, StringComparison.OrdinalIgnoreCase));
}
