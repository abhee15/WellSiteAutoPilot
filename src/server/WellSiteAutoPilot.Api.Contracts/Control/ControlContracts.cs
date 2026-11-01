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
    int WindowSeconds);

public sealed record AutonomyPolicyResponse(
    Guid ConfiguredLogicId,
    bool Enabled,
    string SinkKind,
    IReadOnlyCollection<AutonomyCommandLimitDto> Limits,
    int MaxActionsPerWindow,
    int WindowSeconds,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

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
    DateTimeOffset? CompletedAtUtc);
