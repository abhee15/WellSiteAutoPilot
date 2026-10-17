using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Recommendations;

public sealed partial class RecommendationRepository
{
    public async Task<RecommendationRecord?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.Recommendations(dbContext, subject.UserId, WellSitePermissions.RecommendationsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(recommendation => recommendation.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<RecommendationRecord>> ListAuthorizedAsync(AccessSubject subject,
        RecommendationStatus? status, int limit, Guid? configuredLogicId = null, Guid? assetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = ScopedAccessQueries.Recommendations(dbContext, subject.UserId, WellSitePermissions.RecommendationsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow());
        if (status is { } value) query = query.Where(recommendation => recommendation.Status == value.ToString());
        if (configuredLogicId is { } logic) query = query.Where(recommendation => recommendation.ConfiguredLogicId == logic);
        if (assetId is { } asset) query = query.Where(recommendation => recommendation.AssetId == asset);
        return (await query.OrderByDescending(recommendation => recommendation.CreatedAtUtc).ThenByDescending(recommendation => recommendation.Id)
            .Take(limit).ToArrayAsync(cancellationToken)).Select(ToDomain).ToArray();
    }
}
