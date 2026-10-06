namespace WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;

public sealed record ConfiguredLogicAssetBindingRequest(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record ConfiguredLogicDataBindingRequest(
    string RequirementId,
    Guid AssetId,
    string ProviderId,
    string ProviderAssetExternalId,
    string ProviderMappingJson);

public sealed record ConfiguredLogicScheduleRequest(
    bool Enabled,
    int CadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId);

public sealed record CreateConfiguredLogicRequest(
    string Name,
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingRequest> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingRequest> DataBindings,
    ConfiguredLogicScheduleRequest? Schedule);

public sealed record CreateConfiguredLogicRevisionRequest(
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingRequest> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingRequest> DataBindings,
    ConfiguredLogicScheduleRequest? Schedule);

public sealed record ConfiguredLogicAssetBindingResponse(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record ConfiguredLogicDataBindingResponse(
    string RequirementId,
    Guid AssetId,
    string ProviderId,
    string ProviderAssetExternalId,
    string ProviderMappingJson);

public sealed record ConfiguredLogicScheduleResponse(
    bool Enabled,
    int CadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId);

public sealed record ConfiguredLogicRevisionResponse(
    Guid Id,
    int RevisionNumber,
    string ModuleId,
    string ModuleVersion,
    string ModuleManifestJson,
    string Mode,
    string ParametersJson,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ValidatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    ConfiguredLogicScheduleResponse? Schedule,
    IReadOnlyCollection<ConfiguredLogicAssetBindingResponse> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingResponse> DataBindings);

public sealed record ConfiguredLogicResponse(
    Guid Id,
    string Name,
    Guid? ActiveRevisionId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<ConfiguredLogicRevisionResponse> Revisions);
