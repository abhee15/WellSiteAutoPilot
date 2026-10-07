namespace WellSiteAutoPilot.Messaging.Contracts.Execution;

public sealed record ExecutionRequestedV1(
    Guid ExecutionId,
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid AssetId,
    string AssetExternalId,
    string Quantity,
    string Mode,
    DateTimeOffset RequestedAtUtc);

public sealed record ExecutionCompletedV1(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ResultCode,
    string? OutputJson);

public sealed record ExecutionFailedV1(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FailedAtUtc,
    string FailureCode,
    bool RequiresAttention);


public sealed record ExecutionAssetBindingV2(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record ExecutionInputBindingV2(
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

public sealed record ExecutionRequestedV2(
    Guid ExecutionId,
    Guid ConfiguredLogicId,
    Guid ConfigurationRevisionId,
    string ModuleId,
    string ModuleVersion,
    string Mode,
    string ParametersJson,
    IReadOnlyCollection<ExecutionAssetBindingV2> Assets,
    IReadOnlyCollection<ExecutionInputBindingV2> Inputs,
    DateTimeOffset RequestedAtUtc);

public sealed record ExecutionCompletedV2(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ResultCode,
    string OutputJson);

public sealed record ExecutionFailedV2(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FailedAtUtc,
    string FailureCode,
    bool RequiresAttention);
