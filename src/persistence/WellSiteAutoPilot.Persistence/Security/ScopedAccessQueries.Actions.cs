using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Persistence.Security;

public static partial class ScopedAccessQueries
{
    public static IQueryable<ConfiguredLogicActions> ConfiguredLogicActions(WellSiteAutoPilotDbContext db,
        AccessSubject subject, IReadOnlyCollection<Guid> ids, DateTimeOffset nowUtc)
    {
        var readable = ConfiguredLogic(db, subject.UserId, WellSitePermissions.ConfiguredLogicRead,
            subject.WindowsGroups, nowUtc);
        var manageable = ConfiguredLogic(db, subject.UserId, WellSitePermissions.ConfiguredLogicManage,
            subject.WindowsGroups, nowUtc);
        var requestAssets = Assets(db, subject.UserId, WellSitePermissions.ExecutionsRequest, subject.WindowsGroups, nowUtc);
        var revisions = db.ConfiguredLogicRevisions.AsNoTracking();
        var targets = db.ConfiguredLogicAssetBindings.AsNoTracking();
        var inputs = db.ConfiguredLogicDataBindings.AsNoTracking();
        // Each read predicate covers every historical target and input. Run
        // authority additionally covers every target/input of the ACTIVE
        // revision; management authority is a different, all-history check.
        return readable.Where(definition => ids.Contains(definition.Id)).OrderBy(definition => definition.Id).Select(definition =>
            new ConfiguredLogicActions(definition.Id,
                manageable.Any(managed => managed.Id == definition.Id),
                revisions.Any(revision => revision.ConfiguredLogicId == definition.Id &&
                    revision.Id == definition.ActiveRevisionId &&
                    revision.Status == nameof(ConfiguredLogicRevisionStatus.Active) &&
                    revision.Mode == nameof(ExecutionMode.Shadow) &&
                    targets.Any(target => target.RevisionId == revision.Id) &&
                    !targets.Any(target => target.RevisionId == revision.Id &&
                        !requestAssets.Any(asset => asset.Id == target.AssetId)) &&
                    !inputs.Any(input => input.RevisionId == revision.Id &&
                        !requestAssets.Any(asset => asset.Id == input.AssetId)))));
    }

    public static IQueryable<RecommendationActions> RecommendationActions(WellSiteAutoPilotDbContext db,
        AccessSubject subject, IReadOnlyCollection<Guid> ids, DateTimeOffset nowUtc)
    {
        var readable = Recommendations(db, subject.UserId, WellSitePermissions.RecommendationsRead,
            subject.WindowsGroups, nowUtc);
        var decidable = Recommendations(db, subject.UserId, WellSitePermissions.RecommendationsDecide,
            subject.WindowsGroups, nowUtc);
        return readable.Where(recommendation => ids.Contains(recommendation.Id)).OrderBy(recommendation => recommendation.Id).Select(recommendation =>
            new RecommendationActions(recommendation.Id, decidable.Any(decision => decision.Id == recommendation.Id)));
    }
}
