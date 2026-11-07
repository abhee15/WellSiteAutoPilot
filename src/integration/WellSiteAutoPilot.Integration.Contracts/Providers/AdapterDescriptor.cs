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
/// <paramref name="MinimumScheduleSeconds"/> is the shortest interval at which the provider should be read on a schedule;
/// 0 means the adapter sets no floor beyond the Server's own. Set it only from the provider's stated limit.
/// <paramref name="Commands"/> are the commands this adapter can carry out (their shape, never a limit); null or empty
/// means none, and the Command capability is advertised exactly when there are some.
/// </summary>
public sealed record AdapterDescriptor(
    string Key,
    string DisplayName,
    string Description,
    int SettingsVersion,
    IReadOnlyList<AdapterSettingField> Fields,
    IReadOnlyList<ProviderCapability> Capabilities,
    IReadOnlyList<DiscoveryField>? GroupingFields = null,
    int MinimumScheduleSeconds = 0,
    IReadOnlyList<AdapterCommandDefinition>? Commands = null,
    IReadOnlyList<AdapterOperationDescriptor>? Operations = null,
    IReadOnlyList<AdapterCapabilityGroup>? OperationGroups = null)
{
    /// <summary>Explicit read declarations only. Import and control capabilities are never Operations rows.</summary>
    public IReadOnlyList<AdapterOperationDescriptor> ReadOperations()
    {
        var declared = Operations ?? [];
        // A duplicate key cannot identify one typed contract unambiguously. Exclude every
        // conflicting declaration instead of relying on ordering or breaking the reference UI.
        var duplicateKeys = declared.GroupBy(operation => operation.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        return declared.Where(operation => !string.IsNullOrWhiteSpace(operation.Key) &&
            !duplicateKeys.Contains(operation.Key) &&
            (operation.Capability is ProviderCapabilityKind.CurrentData or ProviderCapabilityKind.HistoricalData or
                ProviderCapabilityKind.Alarms or ProviderCapabilityKind.Calculation or ProviderCapabilityKind.ConfigurationData) &&
            Capabilities.Any(capability => capability.Kind == operation.Capability) &&
            Version.TryParse(operation.ContractVersion, out var version) && version.Major == 1)
            .ToArray();
    }

    /// <summary>Only versioned groups with complete, unique references to installed reads are public.</summary>
    public IReadOnlyList<AdapterCapabilityGroup> ReadCapabilityGroups()
    {
        var keys = ReadOperations().Select(operation => operation.Key).ToHashSet(StringComparer.Ordinal);
        var groups = OperationGroups ?? [];
        var duplicateKeys = groups.GroupBy(group => group.Key, StringComparer.Ordinal).Where(group => group.Count() != 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        return groups.Where(group => !duplicateKeys.Contains(group.Key) && !string.IsNullOrWhiteSpace(group.Key) &&
            !string.IsNullOrWhiteSpace(group.DisplayName) && Version.TryParse(group.ContractVersion, out var version) && version.Major == 1 &&
            group.OperationKeys.Count > 0 && group.OperationKeys.Distinct(StringComparer.Ordinal).Count() == group.OperationKeys.Count &&
            group.OperationKeys.All(keys.Contains)).ToArray();
    }

    /// <summary>True when the adapter can list what the provider holds.</summary>
    public bool CanDiscover => Capabilities.Any(item => item.Kind == ProviderCapabilityKind.AssetDiscovery);
}

/// <summary>A fact about a discovered object that people may group or filter by, for example ForeSite's well type.</summary>
/// <summary>
/// <paramref name="EmptyGroupName"/> names the group for objects with no value for this fact (null gives a generic
/// "Unspecified: label"). <paramref name="AsAttribute"/> says whether the fact is kept as an Asset attribute people can
/// filter on; a fact that only shapes the tree (ForeSite's asset name) can leave it off.
/// <paramref name="IdentityMetadataKey"/> identifies the companion source-ID fact used for stable group identity.
/// </summary>
public sealed record DiscoveryField(string Key, string Label, string? EmptyGroupName = null, bool AsAttribute = true, string? IdentityMetadataKey = null);

public sealed record AdapterSettingProblem(string FieldKey, string Message);
