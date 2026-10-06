namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public sealed record ConfiguredLogicAssetBindingCommand(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record ConfiguredLogicDataBindingCommand(
    string RequirementId,
    Guid AssetId,
    string ProviderId,
    string ProviderAssetExternalId,
    string ProviderMappingJson);

public sealed record ConfiguredLogicScheduleCommand(
    bool Enabled,
    int CadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId);

public sealed record CreateConfiguredLogicCommand(
    string Name,
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingCommand> DataBindings,
    ConfiguredLogicScheduleCommand? Schedule);

public sealed record CreateConfiguredLogicRevisionCommand(
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingCommand> DataBindings,
    ConfiguredLogicScheduleCommand? Schedule);
