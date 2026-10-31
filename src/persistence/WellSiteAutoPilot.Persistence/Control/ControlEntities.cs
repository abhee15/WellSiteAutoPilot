namespace WellSiteAutoPilot.Persistence.Control;

public sealed class AutonomyPolicyEntity
{
    public Guid ConfiguredLogicId { get; init; }
    public bool Enabled { get; set; }
    public string SinkKind { get; set; } = string.Empty;
    public string LimitsJson { get; set; } = "[]";
    public int MaxActionsPerWindow { get; set; }
    public int WindowSeconds { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool AllowApprovedRecommendations { get; set; }
}

public sealed class ControlActionEntity
{
    public Guid Id { get; init; }
    public Guid RecommendationId { get; init; }
    public Guid ExecutionId { get; init; }
    public Guid ConfiguredLogicId { get; init; }
    public Guid AssetId { get; init; }
    public string Command { get; init; } = string.Empty;
    public string? Quantity { get; init; }
    public decimal? Value { get; init; }
    public string? Unit { get; init; }
    public string Status { get; set; } = string.Empty;
    public string GateCode { get; set; } = string.Empty;
    public string Sink { get; init; } = string.Empty;
    public string? SinkReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Origin { get; init; } = "Autonomous";
    public decimal? ReadbackValue { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? ResolutionNote { get; set; }
}

public sealed class ControlKillSwitchEntity
{
    public int Id { get; init; } = 1;
    public bool Engaged { get; set; }
    public string? ChangedBy { get; set; }
    public DateTimeOffset? ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
}
