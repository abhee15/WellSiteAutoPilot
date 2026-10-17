using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository
{
    public async Task<ExecutionRecord?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.Executions(dbContext, subject.UserId, WellSitePermissions.ExecutionsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(execution => execution.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<ExecutionRecord>> ListAuthorizedAsync(AccessSubject subject,
        ExecutionStatus? status, int limit, Guid? configuredLogicId = null, Guid? assetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = ScopedAccessQueries.Executions(dbContext, subject.UserId, WellSitePermissions.ExecutionsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow());
        if (status is { } value) query = query.Where(execution => execution.Status == value.ToString());
        if (configuredLogicId is { } logic) query = query.Where(execution => execution.LogicInstanceId == logic);
        if (assetId is { } asset)
            query = query.Where(execution => dbContext.ExecutionAssetScopes.Any(
                scope => scope.ExecutionId == execution.Id && scope.AssetId == asset));
        return (await query.OrderByDescending(execution => execution.RequestedAtUtc).ThenBy(execution => execution.Id)
            .Take(limit).ToArrayAsync(cancellationToken)).Select(ToDomain).ToArray();
    }
}
