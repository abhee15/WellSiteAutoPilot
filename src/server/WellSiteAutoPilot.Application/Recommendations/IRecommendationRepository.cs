using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<int> AddMissingAsync(
        IReadOnlyCollection<RecommendationRecord> recommendations,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RecommendationRecord>> ListByExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}
