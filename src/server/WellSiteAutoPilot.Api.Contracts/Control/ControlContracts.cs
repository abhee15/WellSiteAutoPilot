namespace WellSiteAutoPilot.Api.Contracts.Control;

public sealed record AutonomyCommandLimitDto(
    string Command,
    string? Unit,
    decimal? MinimumValue,
    decimal? MaximumValue);

public sealed record SetAutonomyPolicyRequest(
    bool Enabled,
    IReadOnlyCollection<AutonomyCommandLimitDto> Limits,
    int MaxActionsPerWindow,
    int WindowSeconds,
    bool AllowApprovedRecommendations = false);

public sealed record AutonomyPolicyResponse(
    Guid ConfiguredLogicId,
    bool Enabled,
    string SinkKind,
    IReadOnlyCollection<AutonomyCommandLimitDto> Limits,
    int MaxActionsPerWindow,
    int WindowSeconds,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc,
    bool AllowApprovedRecommendations);

public sealed record SetKillSwitchRequest(bool Engaged, string? Reason);

public sealed record KillSwitchResponse(
    bool Engaged,
    string? ChangedBy,
    DateTimeOffset? ChangedAtUtc,
    string? Reason,
    bool AutonomousControlEnabled);

public sealed record ControlActionResponse(
    Guid Id,
    Guid RecommendationId,
    Guid ExecutionId,
    Guid ConfiguredLogicId,
    Guid AssetId,
    string Command,
    string? Quantity,
    decimal? Value,
    string? Unit,
    string Status,
    string GateCode,
    string Sink,
    string? SinkReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Origin,
    decimal? ReadbackValue,
    DateTimeOffset? VerifiedAtUtc,
    string? ResolutionNote,
    // True when the caller may verify or resolve this action (control.manage and the right to decide its recommendation).
    // Only the list endpoint computes it; other responses leave it false.
    bool CanManage = false);

// An operator's attestation for an action nobody can verify: Applied=true records it as done, false as not done.
public sealed record ResolveControlActionRequest(bool Applied, string Note);

public sealed record ExecuteApprovedRecommendationResponse(
    bool Executed,
    string? RefusalCode,
    ControlActionResponse? Action);
