namespace WellSiteAutoPilot.Server.Contracts;

public sealed record RecommendationDecisionRequest(string Decision, string? Reason);

public sealed record RecommendationDecisionResponse(
    Guid Id,
    string Status,
    string? DecisionBy,
    DateTimeOffset? DecisionAtUtc,
    string? DecisionReason);
