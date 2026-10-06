namespace WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;

public sealed record ConfiguredLogicAssetBindingRequest(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record CreateConfiguredLogicRequest(
    string Name,
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingRequest> AssetBindings);

public sealed record CreateConfiguredLogicRevisionRequest(
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingRequest> AssetBindings);

public sealed record ConfiguredLogicAssetBindingResponse(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

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
    IReadOnlyCollection<ConfiguredLogicAssetBindingResponse> AssetBindings);

public sealed record ConfiguredLogicResponse(
    Guid Id,
    string Name,
    Guid? ActiveRevisionId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<ConfiguredLogicRevisionResponse> Revisions);
