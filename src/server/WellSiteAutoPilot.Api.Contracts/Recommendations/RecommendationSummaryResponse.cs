namespace WellSiteAutoPilot.Api.Contracts.Recommendations;

/// <summary>
/// Authorized operator-facing projection. Deliberately omits IntentJson,
/// raw provider mappings, and any control execution credentials.
/// </summary>
public sealed record RecommendationSummaryResponse(
    Guid Id,
    Guid ExecutionId,
    int IntentIndex,
    Guid ConfiguredLogicId,
    Guid ConfigurationRevisionId,
    string ModuleId,
    string ModuleVersion,
    Guid AssetId,
    string Code,
    string Command,
    string? Quantity,
    decimal? SuggestedValue,
    string? Unit,
    string ReasonCode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? DecisionBy,
    DateTimeOffset? DecisionAtUtc,
    string? DecisionReason,
    Guid? ControlActionId)
{
    public bool CanDecide { get; init; }
}
