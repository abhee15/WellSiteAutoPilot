using WellSiteAutoPilot.Api.Contracts.Executions;

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
    ConfiguredLogicScheduleRequest? Schedule,
    ConfiguredLogicViewRequest? View = null,
    ExecutionMode? Mode = null);

public sealed record CreateConfiguredLogicRevisionRequest(
    string ModuleManifestJson,
    string ParametersJson,
    IReadOnlyCollection<ConfiguredLogicAssetBindingRequest> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingRequest> DataBindings,
    ConfiguredLogicScheduleRequest? Schedule,
    ConfiguredLogicViewRequest? View = null,
    ExecutionMode? Mode = null);

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
    ExecutionMode Mode,
    string ParametersJson,
    ConfiguredLogicRevisionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ValidatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    ConfiguredLogicScheduleResponse? Schedule,
    IReadOnlyCollection<ConfiguredLogicAssetBindingResponse> AssetBindings,
    IReadOnlyCollection<ConfiguredLogicDataBindingResponse> DataBindings,
    ConfiguredLogicViewResponse? View = null);

public sealed record ConfiguredLogicResponse(
    Guid Id,
    string Name,
    Guid? ActiveRevisionId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<ConfiguredLogicRevisionResponse> Revisions)
{
    public bool CanManage { get; init; }
    public bool CanRequestShadow { get; init; }
}

public sealed record ConfiguredLogicViewRequest(Guid ViewId, int ExpectedRevision, string Role);
public sealed record ConfiguredLogicViewResponse(Guid ViewId, int Revision, Guid ConnectionId, string Role);
