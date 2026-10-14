using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Recommendations;

/// <summary>
/// Read-only recommendation access: validation and resource scoping are
/// applied before the persistence page limit is evaluated.
/// </summary>
public sealed class RecommendationQueryService(IRecommendationRepository repository)
{
    public async Task<IReadOnlyCollection<RecommendationRecord>> ListAsync(
        string? status,
        int limit,
        IReadOnlyCollection<Guid>? authorizedAssetIds,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_LIST_LIMIT_INVALID",
                FailureKind.Validation,
                "Recommendation list limit must be between 1 and 100.");
        }

        RecommendationStatus? statusValue = null;
        if (status is not null)
        {
            if (string.IsNullOrWhiteSpace(status) ||
                !Enum.TryParse<RecommendationStatus>(
                    status, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                throw new WellSiteAutoPilotException(
                    "RECOMMENDATION_STATUS_INVALID",
                    FailureKind.Validation,
                    "Unknown recommendation status filter.");
            }

            statusValue = parsedStatus;
        }

        return await repository.ListAsync(
            statusValue,
            limit,
            authorizedAssetIds,
            cancellationToken);
    }

    public async Task<RecommendationRecord> GetRequiredAsync(
        Guid recommendationId,
        IReadOnlyCollection<Guid>? authorizedAssetIds,
        CancellationToken cancellationToken = default)
    {
        if (recommendationId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_ID_INVALID",
                FailureKind.Validation,
                "A recommendation ID is required.");
        }

        var item = await repository.GetAsync(recommendationId, cancellationToken);

        // Mask out-of-scope records as not found to avoid revealing identities.
        if (item is null ||
            (authorizedAssetIds is not null && !authorizedAssetIds.Contains(item.AssetId)))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_NOT_FOUND",
                FailureKind.NotFound,
                "Recommendation was not found.");
        }

        return item;
    }
}
