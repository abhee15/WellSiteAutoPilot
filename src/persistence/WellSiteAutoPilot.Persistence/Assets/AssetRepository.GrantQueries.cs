using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository
{
    public async Task<AssetTypeDefinition?> GetAssetTypeAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.AssetTypes(dbContext, subject, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(type => type.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAuthorizedAsync(AccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return (await ScopedAccessQueries.AssetTypes(dbContext, subject, (clock ?? TimeProvider.System).GetUtcNow())
            .OrderBy(type => type.DisplayName).ThenBy(type => type.Id).ToArrayAsync(cancellationToken))
            .Select(ToDomain).ToArray();
    }

    public async Task<Asset?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(asset => asset.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public Task<IReadOnlyCollection<Asset>> ListAuthorizedAsync(AccessSubject subject,
        Guid? assetTypeId, Guid? parentAssetId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        return ListCoreAsync(ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow()), assetTypeId, parentAssetId, limit, cancellationToken);
    }

    private const int MaximumPathDepth = 32;

    private sealed record AncestorRef(string Name, Guid? ParentId);

    private IQueryable<AssetEntity> VisibleAssets(AccessSubject subject) =>
        ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow());

    public Task<IReadOnlyCollection<AssetTreeNode>> ListChildrenAuthorizedAsync(AccessSubject subject,
        Guid? parentAssetId, int offset, int limit, CancellationToken cancellationToken = default) =>
        ListChildrenAuthorizedAsync(subject, parentAssetId, null, offset, limit, cancellationToken);

    public async Task<IReadOnlyCollection<AssetOwnerSummary>> ListOwnersAuthorizedAsync(AccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var visible = VisibleAssets(subject);
        var topLevel = visible.Where(item => item.ParentAssetId == null ||
            !visible.Any(parent => parent.Id == item.ParentAssetId));
        var totals = await visible.GroupBy(item => item.OwnerConnectionId)
            .Select(group => new { Owner = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        var tops = (await topLevel.GroupBy(item => item.OwnerConnectionId)
            .Select(group => new { Owner = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken))
            .ToDictionary(row => row.Owner ?? Guid.Empty, row => row.Count);
        var ids = totals.Where(row => row.Owner != null).Select(row => row.Owner!.Value).ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, (string Name, bool IsEnabled)>()
            : (await dbContext.Connections.AsNoTracking().Where(item => ids.Contains(item.Id))
                .Select(item => new { item.Id, item.Name, item.IsEnabled }).ToArrayAsync(cancellationToken))
                .ToDictionary(row => row.Id, row => (row.Name, row.IsEnabled));
        return totals
            .Select(row => new AssetOwnerSummary(row.Owner,
                row.Owner is { } owner && names.TryGetValue(owner, out var found) ? found.Name : null,
                row.Owner is { } own && names.TryGetValue(own, out var state) && state.IsEnabled,
                tops.GetValueOrDefault(row.Owner ?? Guid.Empty), row.Count))
            .OrderBy(row => row.ConnectionId == null).ThenBy(row => row.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ConnectionId)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<AssetTreeNode>> ListChildrenAuthorizedAsync(AccessSubject subject,
        Guid? parentAssetId, AssetOwnerScope? owner, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ValidatePage(offset, limit);
        var visible = ApplyOwnerScope(VisibleAssets(subject), owner);
        var level = parentAssetId is { } parentId
            ? visible.Where(item => item.ParentAssetId == parentId)
            : visible.Where(item => item.ParentAssetId == null ||
                !visible.Any(parent => parent.Id == item.ParentAssetId));
        var page = await level
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip(offset).Take(limit)
            .ToArrayAsync(cancellationToken);

        var ids = page.Select(item => item.Id).ToArray();
        var counts = new Dictionary<Guid, int>();
        if (ids.Length > 0)
        {
            var grouped = await visible
                .Where(item => item.ParentAssetId != null && ids.Contains(item.ParentAssetId.Value))
                .GroupBy(item => item.ParentAssetId)
                .Select(group => new { ParentId = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
            foreach (var row in grouped)
            {
                if (row.ParentId is { } rowParent) counts[rowParent] = row.Count;
            }
        }

        return page
            .Select(item => new AssetTreeNode(ToDomain(item), counts.GetValueOrDefault(item.Id)))
            .ToArray();
    }

    public Task<IReadOnlyCollection<AssetSearchHit>> SearchAuthorizedAsync(AccessSubject subject, string text,
        Guid? assetTypeId, int offset, int limit, CancellationToken cancellationToken = default) =>
        SearchAuthorizedAsync(subject, text, assetTypeId, null, offset, limit, cancellationToken);

    public async Task<IReadOnlyCollection<AssetSearchHit>> SearchAuthorizedAsync(AccessSubject subject, string text,
        Guid? assetTypeId, AssetOwnerScope? owner, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ValidatePage(offset, limit);
        var visible = ApplyOwnerScope(VisibleAssets(subject), owner);
        var term = text.Trim();
        var pattern = "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var matches = Guid.TryParse(term, out var exactId)
            ? visible.Where(item => item.Id == exactId || EF.Functions.ILike(item.Name, pattern, "\\"))
            : visible.Where(item => EF.Functions.ILike(item.Name, pattern, "\\"));
        if (assetTypeId is { } typeId) matches = matches.Where(item => item.AssetTypeId == typeId);
        var page = await matches
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip(offset).Take(limit)
            .ToArrayAsync(cancellationToken);

        var known = new Dictionary<Guid, AncestorRef>();
        var requested = new HashSet<Guid>();
        var frontier = page.Where(item => item.ParentAssetId != null)
            .Select(item => item.ParentAssetId.GetValueOrDefault()).ToHashSet();
        for (var depth = 0; depth < MaximumPathDepth && frontier.Count > 0; depth++)
        {
            var ids = frontier.Where(requested.Add).ToArray();
            if (ids.Length == 0) break;
            var found = await visible
                .Where(item => ids.Contains(item.Id))
                .Select(item => new { item.Id, item.Name, item.ParentAssetId })
                .ToArrayAsync(cancellationToken);
            frontier = [];
            foreach (var item in found)
            {
                known[item.Id] = new AncestorRef(item.Name, item.ParentAssetId);
                if (item.ParentAssetId is { } next) frontier.Add(next);
            }
        }

        return page.Select(item =>
        {
            var path = new List<AssetPathSegment>();
            var cursor = item.ParentAssetId;
            while (cursor is { } id && path.Count < MaximumPathDepth && known.TryGetValue(id, out var ancestor))
            {
                path.Insert(0, new AssetPathSegment(id, ancestor.Name));
                cursor = ancestor.ParentId;
            }

            return new AssetSearchHit(ToDomain(item), path);
        }).ToArray();
    }

    private static IQueryable<AssetEntity> ApplyOwnerScope(IQueryable<AssetEntity> query, AssetOwnerScope? owner) =>
        owner is null ? query : owner.ConnectionId is { } connectionId
            ? query.Where(item => item.OwnerConnectionId == connectionId)
            : query.Where(item => item.OwnerConnectionId == null);

    private static void ValidatePage(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
