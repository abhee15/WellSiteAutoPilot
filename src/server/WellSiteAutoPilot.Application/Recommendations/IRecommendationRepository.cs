using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<int> AddMissingAsync(
        IReadOnlyCollection<RecommendationRecord> recommendations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query in authorized asset scope before ordering and limiting results.
    /// Null scope is reserved for authenticated unrestricted administrators.
    /// </summary>
    Task<IReadOnlyCollection<RecommendationRecord>> ListAsync(
        RecommendationStatus? status,
        int limit,
        IReadOnlyCollection<Guid>? authorizedAssetIds,
        CancellationToken cancellationToken = default);

    Task<RecommendationRecord?> GetAsync(
        Guid recommendationId,
        CancellationToken cancellationToken = default);

    // Atomically transition only an undecided, unexpired recommendation.
    // Returns false when another actor already decided or the item is stale.
    Task<bool> TryDecideAsync(
        Guid recommendationId,
        RecommendationStatus decision,
        string decidedBy,
        string? reason,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RecommendationRecord>> ListByExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}
