using System.Data;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed class ExecutionHistoryReader(WellSiteAutoPilotDbContext db, TimeProvider clock) : IExecutionHistoryReader
{
    public async Task<IReadOnlyList<ExecutionRecord>> ListHistoryAuthorizedAsync(AccessSubject subject,
        Guid? assetId, Guid configuredLogicId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 501) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = ScopedAccessQueries.Executions(db, subject.UserId, WellSitePermissions.ExecutionsRead, subject.Groups, clock.GetUtcNow())
            .Where(item => item.LogicInstanceId == configuredLogicId);
        if (assetId is { } asset) query = query.Where(item => db.ExecutionAssetScopes.Any(
            scope => scope.ExecutionId == item.Id && scope.AssetId == asset));
        if (after is { } cursor)
        {
            var previous = await query.SingleOrDefaultAsync(item => item.Id == cursor, cancellationToken);
            // An inaccessible cursor never reveals its existence or broadens the requested scope.
            if (previous is null) return [];
            query = query.Where(item => item.RequestedAtUtc < previous.RequestedAtUtc ||
                (item.RequestedAtUtc == previous.RequestedAtUtc && item.Id.CompareTo(previous.Id) > 0));
        }
        var entities = await query.OrderByDescending(item => item.RequestedAtUtc).ThenBy(item => item.Id)
            .Take(limit).ToArrayAsync(cancellationToken);
        var result = entities.Select(ExecutionRepository.ToDomain).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
