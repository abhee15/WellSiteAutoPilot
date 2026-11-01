using System.Data;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed class AssetDetailReader(WellSiteAutoPilotDbContext db,
    IConfiguredLogicRepository configurations, TimeProvider clock) : IAssetDetailReader
{
    public async Task<bool?> IsGroupingNodeAuthorizedAsync(AccessSubject subject, Guid assetId, CancellationToken cancellationToken)
    {
        return await ScopedAccessQueries.Assets(db, subject.UserId, WellSitePermissions.AssetsRead, subject.Groups, clock.GetUtcNow())
            .Where(asset => asset.Id == assetId)
            .Select(asset => (bool?)(db.ImportedGroups.Any(group => group.AssetId == asset.Id) ||
                db.AssetTypes.Any(type => type.Id == asset.AssetTypeId && type.Key == ConnectionImportService.GroupTypeKey)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConfiguredLogicDefinition>> ListLogicAuthorizedAsync(AccessSubject subject,
        Guid assetId, int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset < 0 || limit is < 1 or > 501) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        // Filter the complete authorized collection before paging, not the workspace's recent snapshot.
        var ids = await ScopedAccessQueries.ConfiguredLogic(db, subject.UserId, WellSitePermissions.ConfiguredLogicRead,
                subject.Groups, clock.GetUtcNow())
            .Where(item => db.ConfiguredLogicRevisions.Any(revision => revision.ConfiguredLogicId == item.Id &&
                db.ConfiguredLogicAssetBindings.Any(binding => binding.RevisionId == revision.Id && binding.AssetId == assetId)))
            .OrderBy(item => item.Name).ThenBy(item => item.Id).Skip(offset).Take(limit)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var result = new List<ConfiguredLogicDefinition>();
        foreach (var id in ids)
            if (await configurations.GetAuthorizedAsync(subject, id, cancellationToken) is { } item) result.Add(item);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ExecutionRecord>> ListHistoryAuthorizedAsync(AccessSubject subject,
        Guid assetId, Guid configuredLogicId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 501) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = ScopedAccessQueries.Executions(db, subject.UserId, WellSitePermissions.ExecutionsRead, subject.Groups, clock.GetUtcNow())
            .Where(item => item.LogicInstanceId == configuredLogicId && db.ExecutionAssetScopes.Any(
                scope => scope.ExecutionId == item.Id && scope.AssetId == assetId));
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
