using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicRevision(
    Guid Id,
    Guid ConfiguredLogicId,
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
    IReadOnlyCollection<ConfiguredLogicAssetBinding> AssetBindings);
