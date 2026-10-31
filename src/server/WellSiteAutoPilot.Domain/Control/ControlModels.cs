namespace WellSiteAutoPilot.Domain.Control;

public enum ControlActionStatus
{
    // Claimed by the gate; the sink has not confirmed. Never auto-redispatched.
    Requested = 0,
    Dispatched = 1,
    Blocked = 2,
    Failed = 3
}

public static class ControlSinkKinds
{
    public const string Simulator = "Simulator";
    public const string Provider = "Provider";

    // Validates and records what would be written; never writes. Only a read-only current-value lookup is allowed.
    public const string DryRun = "DryRun";
}

public sealed record ControlActionRecord(
    Guid Id,
    Guid RecommendationId,
    Guid ExecutionId,
    Guid ConfiguredLogicId,
    Guid AssetId,
    string Command,
    string? Quantity,
    decimal? Value,
    string? Unit,
    ControlActionStatus Status,
    string GateCode,
    string Sink,
    string? SinkReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc = null);

// An allowlist entry is the explicit permission for one command. Bounds are inclusive.
public sealed record AutonomyCommandLimit(
    string Command,
    string? Unit,
    decimal? MinimumValue,
    decimal? MaximumValue);

public sealed record AutonomyPolicy(
    Guid ConfiguredLogicId,
    bool Enabled,
    string SinkKind,
    IReadOnlyCollection<AutonomyCommandLimit> Limits,
    int MaxActionsPerWindow,
    int WindowSeconds,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public sealed record ControlKillSwitch(
    bool Engaged,
    string? ChangedBy,
    DateTimeOffset? ChangedAtUtc,
    string? Reason);
