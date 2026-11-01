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
    /// <summary>
    /// When the live revision is next due, from the server clock: null without a live enabled schedule. It is when a run
    /// is due, not a promise that one starts then.
    /// </summary>
    public DateTimeOffset? NextRunAtUtc { get; init; }
}

public sealed record ConfiguredLogicViewRequest(Guid ViewId, int ExpectedRevision, string Role);
public sealed record ConfiguredLogicViewResponse(Guid ViewId, int Revision, Guid ConnectionId, string Role);

/// <summary>Live operational observations, separate from the immutable revision being reviewed.</summary>
public sealed record ConfiguredLogicActivityResponse(Guid? ActiveRevisionId, DateTimeOffset ObservedAtUtc,
    DateTimeOffset? NextScheduledAtUtc, ExecutionResponse[] RecentRuns);

/// <summary>Pause or resume the live schedule. The reviewed version is sent in If-Match.</summary>
public sealed record ChangeConfiguredLogicScheduleStateRequest(string? Reason);

/// <summary>Change how often the live schedule runs. The reviewed version is sent in If-Match.</summary>
public sealed record ChangeConfiguredLogicScheduleFrequencyRequest(int CadenceSeconds, string? Reason);

public sealed record ConfiguredLogicScheduleChangeResponse(
    Guid Id,
    int Version,
    ConfiguredLogicScheduleChangeKind Kind,
    ConfiguredLogicScheduleResponse Schedule,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason);

/// <summary>
/// One logic's schedule: what the live revision declares, what is running now, and the changes made since. Saved
/// revisions never change, so a pause or frequency change shows here and not in the revision.
/// </summary>
public sealed record ConfiguredLogicScheduleControlResponse(
    Guid ConfiguredLogicId,
    Guid? ActiveRevisionId,
    string Version,
    ConfiguredLogicScheduleResponse? Declared,
    ConfiguredLogicScheduleResponse? Effective,
    bool DiffersFromRevision,
    DateTimeOffset? NextRunAtUtc,
    int MinimumCadenceSeconds,
    int MaximumCadenceSeconds,
    bool CanManage,
    IReadOnlyCollection<ConfiguredLogicScheduleChangeResponse> Changes);
