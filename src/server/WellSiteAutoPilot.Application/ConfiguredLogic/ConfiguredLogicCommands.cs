namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public sealed record ConfiguredLogicAssetBindingCommand(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record CreateConfiguredLogicCommand(
    string Name,
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> AssetBindings);

public sealed record CreateConfiguredLogicRevisionCommand(
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> AssetBindings);
