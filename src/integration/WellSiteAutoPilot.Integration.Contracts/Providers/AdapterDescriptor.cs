namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public enum AdapterSettingKind
{
    Text = 0,
    Url = 1,
    WholeNumber = 2,
    YesNo = 3
}

/// <summary>One setting an adapter needs on a connection. Deliberately flat: no nesting, conditions or schema language.</summary>
public sealed record AdapterSettingField(
    string Key,
    string Label,
    string HelpText,
    AdapterSettingKind Kind,
    bool Required,
    int MaxLength = 256);

/// <summary>
/// What one adapter is and what a connection to it needs. The adapter owns this, so the Server and the web UI stay
/// provider-neutral. Raise <see cref="SettingsVersion"/> when the fields change, so older connections are reviewed.
/// </summary>
public sealed record AdapterDescriptor(
    string Key,
    string DisplayName,
    string Description,
    int SettingsVersion,
    IReadOnlyList<AdapterSettingField> Fields,
    IReadOnlyList<ProviderCapability> Capabilities,
    IReadOnlyList<DiscoveryField>? GroupingFields = null)
{
    /// <summary>True when the adapter can list what the provider holds.</summary>
    public bool CanDiscover => Capabilities.Any(item => item.Kind == ProviderCapabilityKind.AssetDiscovery);
}

/// <summary>A fact about a discovered object that people may group or filter by, for example ForeSite's well type.</summary>
public sealed record DiscoveryField(string Key, string Label);

public sealed record AdapterSettingProblem(string FieldKey, string Message);
