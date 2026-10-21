using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository
{
    public async Task<AssetViewNodes> QueryViewAsync(AccessSubject subject, AssetViewDefinition definition,
        IReadOnlyList<string> path, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(path);
        ValidatePage(offset, limit);
        if (path.Count > definition.Grouping.Count) throw InvalidPath();

        var matches = ApplyDefinition(VisibleAssets(subject), definition);
        for (var index = 0; index < path.Count; index++) matches = ApplyGroup(matches, definition.Grouping[index], path[index]);

        if (path.Count == definition.Grouping.Count)
        {
            var page = await matches.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip(offset).Take(limit).ToArrayAsync(cancellationToken);
            return new AssetViewNodes(null, [], page.Select(item => new AssetTreeNode(ToDomain(item), 0)).ToArray());
        }

        var level = definition.Grouping[path.Count];
        var groups = await GroupsAsync(matches, level, cancellationToken);
        return new AssetViewNodes(level,
            groups.OrderBy(group => group.Label, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Skip(offset).Take(limit).ToArray(), []);
    }

    private static IQueryable<AssetEntity> ApplyDefinition(IQueryable<AssetEntity> query, AssetViewDefinition definition)
    {
        if (definition.Connections.Count > 0)
        {
            var owners = definition.Connections.Where(item => item != AssetViewDefinitionRules.Standalone)
                .Select(Guid.Parse).ToArray();
            var standalone = definition.Connections.Contains(AssetViewDefinitionRules.Standalone);
            query = query.Where(item => (item.OwnerConnectionId != null && owners.Contains(item.OwnerConnectionId.Value)) ||
                (standalone && item.OwnerConnectionId == null));
        }
        if (definition.AssetTypeIds.Count > 0)
        {
            var types = definition.AssetTypeIds.ToArray();
            query = query.Where(item => types.Contains(item.AssetTypeId));
        }
        if (definition.Activity == "active") query = query.Where(item => item.IsActive);
        else if (definition.Activity == "inactive") query = query.Where(item => !item.IsActive);
        if (definition.Search is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern, "\\"));
        }
        return query;
    }

    private static IQueryable<AssetEntity> ApplyGroup(IQueryable<AssetEntity> query, string level, string key)
    {
        switch (level)
        {
            case AssetViewDefinitionRules.ByConnection:
                if (key == AssetViewDefinitionRules.Standalone) return query.Where(item => item.OwnerConnectionId == null);
                if (!Guid.TryParse(key, out var owner)) throw InvalidPath();
                return query.Where(item => item.OwnerConnectionId == owner);
            case AssetViewDefinitionRules.ByAssetType:
                if (!Guid.TryParse(key, out var type)) throw InvalidPath();
                return query.Where(item => item.AssetTypeId == type);
            default:
                if (key == "active") return query.Where(item => item.IsActive);
                if (key == "inactive") return query.Where(item => !item.IsActive);
                throw InvalidPath();
        }
    }

    private async Task<IReadOnlyList<AssetViewGroup>> GroupsAsync(IQueryable<AssetEntity> matches, string level,
        CancellationToken cancellationToken)
    {
        if (level == AssetViewDefinitionRules.ByConnection)
        {
            var rows = await matches.GroupBy(item => item.OwnerConnectionId)
                .Select(group => new { Owner = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
            var ids = rows.Where(row => row.Owner != null).Select(row => row.Owner!.Value).ToArray();
            var names = ids.Length == 0
                ? new Dictionary<Guid, string>()
                : await dbContext.Connections.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
            return rows.Select(row => row.Owner is { } owner
                ? new AssetViewGroup(owner.ToString(), names.GetValueOrDefault(owner) ?? "Unknown Connection", row.Count)
                : new AssetViewGroup(AssetViewDefinitionRules.Standalone, "Standalone", row.Count)).ToArray();
        }
        if (level == AssetViewDefinitionRules.ByAssetType)
        {
            var rows = await matches.GroupBy(item => item.AssetTypeId)
                .Select(group => new { Type = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
            var ids = rows.Select(row => row.Type).ToArray();
            var names = await dbContext.AssetTypes.AsNoTracking().Where(item => ids.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
            return rows.Select(row => new AssetViewGroup(row.Type.ToString(),
                names.GetValueOrDefault(row.Type) ?? "Unknown type", row.Count)).ToArray();
        }
        var statuses = await matches.GroupBy(item => item.IsActive)
            .Select(group => new { Active = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        return statuses.Select(row => new AssetViewGroup(row.Active ? "active" : "inactive",
            row.Active ? "Active" : "Inactive", row.Count)).ToArray();
    }

    private static WellSiteAutoPilotException InvalidPath() =>
        new(FailureCodes.ValidationFailed, FailureKind.Validation, "That part of the view does not exist.");
}
