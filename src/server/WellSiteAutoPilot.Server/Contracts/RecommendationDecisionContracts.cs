using WellSiteAutoPilot.Api.Contracts.Recommendations;

namespace WellSiteAutoPilot.Server.Contracts;

public sealed record RecommendationDecisionRequest(RecommendationDecision Decision, string? Reason);

public sealed record RecommendationDecisionResponse(
    Guid Id,
    RecommendationStatus Status,
    string? DecisionBy,
    DateTimeOffset? DecisionAtUtc,
    string? DecisionReason);
