using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Domain.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Adds the readable names behind the ids an audit event records, so the Activity page can say
/// "ForeSite" instead of a GUID. Runs once per page: one query per kind of target on that page and
/// one for the people, never one per row. An id that no longer resolves (the record was deleted)
/// simply has no name, and the event itself is never changed or dropped.
/// </summary>
internal static class AuditEventNames
{
    public static async Task<AuditEventRecord[]> AddAsync(
        WellSiteAutoPilotDbContext db, AuditEventRecord[] events, CancellationToken cancellationToken)
    {
        if (events.Length == 0) return events;

        var targetNames = new Dictionary<(string Kind, Guid Id), string>();
        var byKind = events
            .Where(item => item.TargetId is not null)
            .Select(item => (Kind: KindOf(item.TargetType), Id: ParseId(item.TargetId)))
            .Where(item => item.Kind is not null && item.Id is not null)
            .GroupBy(item => item.Kind!, item => item.Id!.Value);
        foreach (var group in byKind)
        {
            var ids = group.Distinct().ToArray();
            foreach (var (id, name) in await TargetNamesAsync(db, group.Key, ids, cancellationToken))
                targetNames[(group.Key, id)] = name;
        }

        var actorIds = events.Where(item => item.ActorUserId is not null)
            .Select(item => item.ActorUserId!.Value).Distinct().ToArray();
        var actorNames = new Dictionary<Guid, string>();
        if (actorIds.Length > 0)
        {
            var users = await db.Users.AsNoTracking().Where(user => actorIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName, user.IdentityName })
                .ToArrayAsync(cancellationToken);
            foreach (var user in users)
                actorNames[user.Id] = PersonName(user.DisplayName, user.IdentityName);
        }

        return events.Select(item =>
        {
            string? targetName = null;
            if (KindOf(item.TargetType) is { } kind && ParseId(item.TargetId) is { } id &&
                targetNames.TryGetValue((kind, id), out var found))
                targetName = found;
            string? actorName = null;
            if (item.ActorUserId is { } actorId && actorNames.TryGetValue(actorId, out var person))
                actorName = person;
            return item with { TargetName = targetName, ActorName = actorName };
        }).ToArray();
    }

    /// <summary>Target types are recorded in a few spellings ("connection", "Connection", "logic-module").</summary>
    private static string? KindOf(string targetType) => targetType
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant() switch
    {
        "connection" => "connection",
        "configuredlogic" => "configuredlogic",
        "logicmodule" => "logicmodule",
        "asset" => "asset",
        "assetview" => "assetview",
        "assettype" => "assettype",
        "securityuser" => "securityuser",
        "securityrole" => "securityrole",
        _ => null,
    };

    /// <summary>A person is shown by display name, or by their sign-in name when no display name is known.</summary>
    private static string PersonName(string? displayName, string identityName) =>
        string.IsNullOrWhiteSpace(displayName) ? identityName : displayName;

    private static Guid? ParseId(string? text) => Guid.TryParse(text, out var id) ? id : null;

    private static async Task<(Guid Id, string Name)[]> TargetNamesAsync(
        WellSiteAutoPilotDbContext db, string kind, Guid[] ids, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case "connection":
                return (await db.Connections.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.Name)).ToArray();
            case "configuredlogic":
                return (await db.ConfiguredLogicDefinitions.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.Name)).ToArray();
            case "logicmodule":
                return (await db.LogicModules.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.DisplayName, item.Version }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.DisplayName + " v" + item.Version)).ToArray();
            case "asset":
                return (await db.Assets.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.Name)).ToArray();
            case "assetview":
                return (await db.AssetViews.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.Name)).ToArray();
            case "assettype":
                return (await db.AssetTypes.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.DisplayName }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.DisplayName)).ToArray();
            case "securityuser":
                return (await db.Users.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.DisplayName, item.IdentityName }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, PersonName(item.DisplayName, item.IdentityName))).ToArray();
            case "securityrole":
                return (await db.RoleDefinitions.AsNoTracking().Where(item => ids.Contains(item.Id))
                    .Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken))
                    .Select(item => (item.Id, item.Name)).ToArray();
            default:
                return [];
        }
    }
}
