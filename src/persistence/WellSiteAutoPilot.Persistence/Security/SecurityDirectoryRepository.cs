using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WellSiteAutoPilot.Application.Audit;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Audit;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class SecurityDirectoryRepository(WellSiteAutoPilotDbContext db, TimeProvider clock)
    : ISecurityDirectoryRepository
{
    public Task<IReadOnlyCollection<DirectoryGroup>> ListAssignedGroupsAuthorizedAsync(AccessSubject administrator,
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyCollection<DirectoryGroup>>(administrator, async () =>
        {
            var rows = await db.AccessGrants.AsNoTracking().Where(g => g.SubjectKind == "Group" && g.GroupId != null && g.DirectoryId != null)
                .GroupBy(g => new { g.DirectoryId, g.GroupId })
                .Select(g => new { g.Key.DirectoryId, g.Key.GroupId,
                    DisplayName = g.Max(x => x.GroupDisplayName), Domain = g.Max(x => x.GroupDomain) })
                .OrderBy(g => g.DirectoryId).ThenBy(g => g.GroupId).Take(501).ToArrayAsync(cancellationToken);
            if (rows.Length > 500)
                throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_TOO_LARGE", FailureKind.Validation,
                    "The assigned-group list exceeds 500 groups. Use directory search to locate the group.");
            return rows.Select(g => new DirectoryGroup(g.DirectoryId!, g.GroupId!, g.DisplayName ?? g.GroupId!, g.Domain ?? "")).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyCollection<SecurityAssetDirectoryEntry>> ListAssetsAuthorizedAsync(AccessSubject administrator,
        int offset, int limit, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        if (offset is < 0 or > 1000000)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Security Asset directory offset must be between 0 and 1000000.");
        return ReadAsync<IReadOnlyCollection<SecurityAssetDirectoryEntry>>(administrator, async () =>
            await db.Assets.AsNoTracking().OrderBy(asset => asset.Name).ThenBy(asset => asset.Id)
                .Skip(offset).Take(limit)
                .Select(asset => new SecurityAssetDirectoryEntry(asset.Id, asset.Name, asset.IsActive))
                .ToArrayAsync(cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyCollection<SecurityUserDirectoryEntry>> ListUsersAuthorizedAsync(AccessSubject administrator, int limit,
        bool awaitingAccessOnly = false, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        return ReadAsync<IReadOnlyCollection<SecurityUserDirectoryEntry>>(administrator, async () =>
        {
            // A grant counts only while both it and its role are active. Windows-group access is
            // proven per request from the token, so it can never appear in this count.
            var directGrants = db.AccessGrants.AsNoTracking().Where(grant =>
                grant.IsActive && grant.SubjectKind == "User" &&
                db.RoleDefinitions.Any(role => role.Id == grant.RoleId && role.IsActive));
            var rows = db.Users.AsNoTracking().Select(user => new
            {
                User = user,
                Count = directGrants.Count(grant => grant.UserId == user.Id),
            });
            var page = awaitingAccessOnly
                ? rows.Where(row => row.User.IsActive && row.Count == 0)
                    .OrderByDescending(row => row.User.LastSeenAtUtc).ThenBy(row => row.User.Id)
                : rows.OrderBy(row => row.User.IdentityName).ThenBy(row => row.User.Id);
            return (await page.Take(limit).ToArrayAsync(cancellationToken))
                .Select(row => new SecurityUserDirectoryEntry(
                    new UserIdentity(row.User.Id, row.User.IdentityKey, row.User.IdentityName, row.User.DisplayName,
                        row.User.IsActive, row.User.CreatedAtUtc, row.User.LastSeenAtUtc),
                    row.Count))
                .ToArray();
        }, cancellationToken);
    }

    public Task<IReadOnlyCollection<RoleDefinition>> ListRolesAuthorizedAsync(AccessSubject administrator, int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        return ReadAsync(administrator, () => new RoleCatalogRepository(db, clock).ListAsync(limit, cancellationToken), cancellationToken);
    }

    public Task<RoleDefinition?> GetRoleAuthorizedAsync(AccessSubject administrator, Guid roleId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(administrator, () => new RoleCatalogRepository(db, clock).GetAsync(roleId, cancellationToken), cancellationToken);

    public Task<IReadOnlyCollection<AccessGrant>> ListGrantsAuthorizedAsync(AccessSubject administrator, AccessGrantSubject target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ReadAsync(administrator, async () =>
        {
            // Disabled users and retired assignments remain inspectable by
            // administrators. No legacy role parsing or group discovery occurs.
            if (target.UserId is { } id && !await db.Users.AsNoTracking().AnyAsync(user => user.Id == id, cancellationToken))
                throw new WellSiteAutoPilotException("SECURITY_USER_NOT_FOUND", FailureKind.NotFound, "The requested user was not found.");
            return await new AccessGrantRepository(db, clock).ListAsync(target, cancellationToken);
        }, cancellationToken);
    }

    public Task<AuditEventPage> ListAuditEventsAuthorizedAsync(AccessSubject administrator, AuditEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureValid();
        return ReadAsync(administrator, async () =>
        {
            var events = db.AuditEvents.AsNoTracking();
            if (query.ActionPrefix is { } prefix)
            {
                // LIKE wildcards in the caller's text are literals, never patterns.
                var pattern = prefix.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal) + "%";
                events = events.Where(item => EF.Functions.Like(item.Action, pattern, "\\"));
            }
            if (query.TargetType is { } targetType) events = events.Where(item => item.TargetType == targetType);
            if (query.ActorUserId is { } actor) events = events.Where(item => item.ActorUserId == actor);
            if (query.AssetId is { } asset) events = events.Where(item => item.AssetId == asset);
            if (query.FromUtc is { } from) events = events.Where(item => item.OccurredAtUtc >= from);
            if (query.ToUtc is { } to) events = events.Where(item => item.OccurredAtUtc <= to);
            if (query.After is { } after)
                events = events.Where(item => item.OccurredAtUtc < after.OccurredAtUtc ||
                    (item.OccurredAtUtc == after.OccurredAtUtc && item.Id.CompareTo(after.Id) < 0));

            // One extra row tells whether another page exists without a second count query.
            var rows = await events.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
                .Take(query.Limit + 1).ToArrayAsync(cancellationToken);
            var page = rows.Take(query.Limit).Select(item => new AuditEventRecord(
                item.Id, item.OccurredAtUtc, item.ActorUserId, item.ActorIdentity, item.Action, item.TargetType,
                item.TargetId, item.AssetId, item.CorrelationId, item.DetailsJson)).ToArray();
            var next = rows.Length > query.Limit
                ? new AuditEventCursor(page[^1].OccurredAtUtc, page[^1].Id) : null;
            return new AuditEventPage(page, next);
        }, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(AccessSubject administrator, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        await using var transaction = await BeginReadAsync(cancellationToken);
        var access = new RequestAccessRepository(db, clock);
        await RequireAdministratorAsync();
        var result = await read();
        // Database state belongs to one stable snapshot, while a Windows
        // observation must still be fresh after potentially slow hydration.
        await RequireAdministratorAsync();
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;

        async Task RequireAdministratorAsync()
        {
            if (!await access.HasAdministratorAsync(administrator, cancellationToken))
                throw new WellSiteAutoPilotException("SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                    "A current protected administrator assignment is required.");
        }
    }

    private async Task<IDbContextTransaction?> BeginReadAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not { } current)
            return await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (current.GetDbTransaction().IsolationLevel is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
            throw new InvalidOperationException("Authorized security directory reads require a stable database snapshot.");
        return null;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 500)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Security directory list limit must be between 1 and 500.");
    }
}
