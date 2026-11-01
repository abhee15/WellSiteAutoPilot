namespace WellSiteAutoPilot.Domain.Control;

public enum ControlActionStatus
{
    // Claimed by the gate; the sink has not confirmed. Never auto-redispatched.
    Requested = 0,
    // Accepted by the sink; a readback has not confirmed it yet.
    Dispatched = 1,
    Blocked = 2,
    Failed = 3,
    // The target was read back (or a person attested) and holds the commanded value.
    Verified = 4,
    // The command may or may not have landed (sink error or timeout, readback mismatch). Never assumed either way:
    // only a conclusive readback or a person's resolution settles it. It still counts against the rate limit.
    Ambiguous = 5
}

public enum ControlReadbackState
{
    // The target could not say.
    Unknown = 0,
    Applied = 1,
    NotApplied = 2
}

public static class ControlOrigins
{
    // Decided by the autonomy gate itself (Autonomous mode).
    public const string Autonomous = "Autonomous";

    // A Recommendation-mode recommendation a person approved, then executed through the same gate.
    public const string ApprovedRecommendation = "ApprovedRecommendation";
}

public static class ControlSinkKinds
{
    public const string Simulator = "Simulator";
    public const string Provider = "Provider";
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
    DateTimeOffset? CompletedAtUtc = null,
    string Origin = ControlOrigins.Autonomous,
    decimal? ReadbackValue = null,
    DateTimeOffset? VerifiedAtUtc = null,
    string? ResolutionNote = null);

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
    DateTimeOffset UpdatedAtUtc,
    // Opt-in: lets an operator-approved Recommendation-mode recommendation be executed through the same gate.
    bool AllowApprovedRecommendations = false);

public sealed record ControlKillSwitch(
    bool Engaged,
    string? ChangedBy,
    DateTimeOffset? ChangedAtUtc,
    string? Reason);
