namespace WellSiteAutoPilot.Persistence.Recommendations;

public sealed class RecommendationEntity
{
    public Guid Id { get; init; }
    public Guid ExecutionId { get; init; }
    public int IntentIndex { get; init; }
    public Guid ConfiguredLogicId { get; init; }
    public Guid ConfigurationRevisionId { get; init; }
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string? Quantity { get; init; }
    public decimal? SuggestedValue { get; init; }
    public string? Unit { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string IntentJson { get; init; } = "{}";
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? DecisionBy { get; set; }
    public DateTimeOffset? DecisionAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? ControlActionId { get; set; }
}
