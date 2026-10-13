using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Recommendations;

/// <summary>
/// Validates recommendation decisions without issuing any control command.
/// Authorization and asset-scope checks must be performed by the authenticated API boundary.
/// </summary>
public sealed class RecommendationDecisionService(
    IRecommendationRepository repository,
    TimeProvider timeProvider)
{
    public async Task<RecommendationRecord> DecideAsync(
        Guid recommendationId,
        RecommendationStatus decision,
        string actorIdentity,
        string? reason,
        IReadOnlyCollection<Guid> authorizedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizedAssetIds);

        if (recommendationId == Guid.Empty ||
            decision is not (RecommendationStatus.Approved or RecommendationStatus.Rejected) ||
            string.IsNullOrWhiteSpace(actorIdentity))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_DECISION_INVALID",
                FailureKind.Validation,
                "Recommendation ID, supported decision, and authenticated actor are required.");
        }

        var normalizedActorIdentity = actorIdentity.Trim();
        if (normalizedActorIdentity.Length > 256 || normalizedActorIdentity.Any(char.IsControl) ||
            (reason is not null && (reason.Length > 1024 || reason.Any(char.IsControl))))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_DECISION_METADATA_INVALID",
                FailureKind.Validation,
                "Decision actor or reason contains invalid characters or exceeds storage limits.");
        }

        var snapshot = await repository.GetAsync(recommendationId, cancellationToken)
            ?? throw new WellSiteAutoPilotException(
                "RECOMMENDATION_NOT_FOUND",
                FailureKind.NotFound,
                "Recommendation was not found.");

        if (!authorizedAssetIds.Contains(snapshot.AssetId))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_ASSET_SCOPE_FORBIDDEN",
                FailureKind.Authorization,
                "The authenticated actor is not authorized for the recommendation asset.");
        }

        var now = timeProvider.GetUtcNow();
        if (snapshot.Status is not (RecommendationStatus.Created or RecommendationStatus.Pending) ||
            (snapshot.ExpiresAtUtc is { } expiry && expiry <= now))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_NOT_DECIDABLE",
                FailureKind.Conflict,
                "Recommendation has already been decided or has expired.");
        }

        if (!await repository.TryDecideAsync(
                recommendationId, decision, normalizedActorIdentity, reason, now, cancellationToken))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_DECISION_CONFLICT",
                FailureKind.Conflict,
                "Recommendation was changed or expired during the decision.");
        }

        return (await repository.GetAsync(recommendationId, cancellationToken))!;
    }
}
