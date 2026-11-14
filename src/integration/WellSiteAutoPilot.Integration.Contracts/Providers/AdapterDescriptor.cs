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
    public IReadOnlyList<AdapterOperationDescriptor> ReadOperations() => (Operations ?? [])
        .Where(operation => (operation.Capability is ProviderCapabilityKind.CurrentData or ProviderCapabilityKind.HistoricalData or
            ProviderCapabilityKind.Alarms or ProviderCapabilityKind.Calculation or ProviderCapabilityKind.ConfigurationData or
            ProviderCapabilityKind.ExtensionRead) &&
            Capabilities.Any(capability => capability.Kind == operation.Capability) &&
            Version.TryParse(operation.ContractVersion, out var version) && version.Major == 1)
        .ToArray();

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
            group.OperationKeys.All(keys.Contains) && ValidWorkflows(group)).ToArray();
    }

    /// <summary>Reject ambiguous, unsupported or unbounded workflow metadata.</summary>
    private static bool ValidWorkflows(AdapterCapabilityGroup group)
    {
        if (group.Workflows is null) return true; // Existing groups retain their wire shape.
        if (group.Workflows.Count is < 1 or > 8 || group.Workflows.Any(workflow => workflow is null) ||
            group.Workflows.Select(workflow => workflow.Key).Distinct(StringComparer.Ordinal).Count() != group.Workflows.Count)
            return false;
        return group.Workflows.All(workflow =>
            workflow.Key is { Length: > 0 and <= 64 } && !string.IsNullOrWhiteSpace(workflow.DisplayName) &&
            workflow.DisplayName.Length <= 128 && !string.IsNullOrWhiteSpace(workflow.Purpose) &&
            workflow.Purpose.Length <= 1024 && workflow.Readiness is ("ReferenceOnly" or "Implemented") &&
            Version.TryParse(workflow.ContractVersion, out var version) && version.Major == 1 &&
            workflow.OperationKeys is { Count: >= 1 and <= 32 } &&
            workflow.OperationKeys.Distinct(StringComparer.Ordinal).Count() == workflow.OperationKeys.Count &&
            workflow.OperationKeys.All(key => group.OperationKeys.Contains(key, StringComparer.Ordinal)) &&
            workflow.Steps is { Count: >= 1 and <= 12 } &&
            workflow.Steps.All(step => step is not null) &&
            workflow.Steps.Select(step => step.Key).Distinct(StringComparer.Ordinal).Count() == workflow.Steps.Count &&
            workflow.Steps.All(step => step.Key is { Length: > 0 and <= 64 } &&
                !string.IsNullOrWhiteSpace(step.Description) && step.Description.Length <= 1024 &&
                (step.ProviderApi is null || step.ProviderApi.Length <= 256) &&
                (step.Requirement is null || step.Requirement.Length <= 1024) &&
                (step.OnFailure is null || step.OnFailure.Length <= 1024)));
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
