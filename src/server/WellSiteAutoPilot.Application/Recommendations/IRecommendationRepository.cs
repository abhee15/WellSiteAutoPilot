using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<RecommendationRecord> DecideAuthorizedAsync(AccessSubject subject, Guid recommendationId,
        RecommendationStatus decision, SecurityActorContext actor, string? reason,
        CancellationToken cancellationToken = default);

    Task<RecommendationRecord?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists authorized recommendations, newest first. Scope is applied before the optional configured
    /// Logic and Asset filters and before the limit.
    /// </summary>
    Task<IReadOnlyCollection<RecommendationRecord>> ListAuthorizedAsync(AccessSubject subject, RecommendationStatus? status, int limit,
        Guid? configuredLogicId = null, Guid? assetId = null, CancellationToken cancellationToken = default);

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
