using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class ResourceActionRepository(WellSiteAutoPilotDbContext db, TimeProvider clock)
    : IResourceActionRepository
{
    public async Task<IReadOnlyCollection<ConfiguredLogicActions>> GetConfiguredLogicActionsAsync(AccessSubject subject,
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        Validate(subject, ids);
        if (ids.Count == 0) return [];
        return await ScopedAccessQueries.ConfiguredLogicActions(db, subject, ids, clock.GetUtcNow())
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RecommendationActions>> GetRecommendationActionsAsync(AccessSubject subject,
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        Validate(subject, ids);
        if (ids.Count == 0) return [];
        return await ScopedAccessQueries.RecommendationActions(db, subject, ids, clock.GetUtcNow())
            .ToArrayAsync(cancellationToken);
    }

    private static void Validate(AccessSubject subject, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > 500 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Action capability IDs must be distinct, non-empty and limited to 500 per page.");
    }
}
