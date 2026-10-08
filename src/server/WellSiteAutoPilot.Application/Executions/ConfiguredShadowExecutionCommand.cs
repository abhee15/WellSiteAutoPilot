using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Executions;

public sealed record ConfiguredExecutionAssetCommand(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record ConfiguredExecutionInputCommand(
    string RequirementId,
    Guid AssetId,
    string ProviderId,
    string ProviderAssetExternalId,
    string Quantity,
    string Access,
    string? CanonicalUnit,
    int? MaximumAgeSeconds,
    bool AllowUncertainQuality,
    string ProviderMappingJson);

public sealed record ConfiguredShadowExecutionCommand(
    Guid ConfiguredLogicId,
    Guid ConfigurationRevisionId,
    string ModuleId,
    string ModuleVersion,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredExecutionAssetCommand> Assets,
    IReadOnlyCollection<ConfiguredExecutionInputCommand> Inputs,
    ExecutionTriggerKind Trigger,
    DateTimeOffset? ScheduledForUtc);
