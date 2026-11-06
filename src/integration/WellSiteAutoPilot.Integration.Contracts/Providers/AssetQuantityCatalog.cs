namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>What a provider says it can serve for one asset: one entry per quantity, with the unit it would report.</summary>
public sealed record QuantityCatalogEntry(
    string Key, string DisplayName, string Kind, string Unit, decimal? Min = null, decimal? Max = null);

/// <summary>
/// The quantities available for one asset, with the facts that decide applicability. A module requirement that
/// names a key missing here fails validation instead of failing a run.
/// </summary>
public sealed record AssetQuantityCatalog(
    string AssetExternalId, string? DeviceType, string? WellType, IReadOnlyList<QuantityCatalogEntry> Entries);
