using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>Explicit user and Windows-group entitlements, currently audit-only until cutover.</summary>
public sealed class AccessGrantRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null)
    : IAccessGrantRepository
{
    private static readonly JsonSerializerOptions AuditOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<AccessGrant>> ListAsync(
        AccessGrantSubject subject, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.AccessGrants.AsNoTracking()
            .Where(item => item.SubjectKind == subject.Kind.ToString() && item.UserId == subject.UserId && item.GroupId == subject.GroupId && item.DirectoryId == subject.DirectoryId)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Take(501)
            .ToArrayAsync(cancellationToken);

        // Never silently truncate entitlements: the inspector and UI must
        // not claim a partial set represents the user's access.
        if (rows.Length > 500)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_LIST_TOO_LARGE", FailureKind.Validation,
                "The subject has too many assignment records for a complete inspection.");
        }

        if (rows.Length == 0)
        {
            return [];
        }

        var ids = rows.Select(item => item.Id).ToArray();
        var bindings = await dbContext.AccessGrantAssets.AsNoTracking()
            .Where(item => ids.Contains(item.GrantId))
            .OrderBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return rows.Select(item => ToDomain(item,
            bindings.Where(binding => binding.GrantId == item.Id)
                .Select(binding => binding.AssetId))).ToArray();
    }

    public Task<AccessGrant> CreateAuthorizedAsync(AccessGrantSubject subject, CreateAccessGrantCommand command,
        AccessSubject administrator, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        GrantAdministrationActor.ExecuteAsync(() => CreateCoreAsync(subject, command,
            GrantAdministrationActor.Bind(administrator, actor), nowUtc, administrator, cancellationToken));

    public Task<AccessGrant> DeactivateAuthorizedAsync(AccessGrantSubject subject, Guid grantId, long expectedRevision,
        AccessSubject administrator, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        GrantAdministrationActor.ExecuteAsync(() => DeactivateCoreAsync(subject, grantId, expectedRevision,
            GrantAdministrationActor.Bind(administrator, actor), nowUtc, administrator, cancellationToken));

    public Task<AccessGrant> CreateAsync(
        AccessGrantSubject subject, CreateAccessGrantCommand command,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) => CreateCoreAsync(subject, command, actor, nowUtc, null, cancellationToken);

    private async Task<AccessGrant> CreateCoreAsync(AccessGrantSubject subject, CreateAccessGrantCommand command,
        SecurityActorContext actor, DateTimeOffset nowUtc, AccessSubject? administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.AssetIds);
        ArgumentNullException.ThrowIfNull(actor);
        nowUtc = nowUtc.ToUniversalTime();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        if (administrator is not null)
        {
            await RequireAdministrationAsync(administrator, actor, cancellationToken);
            await dbContext.RoleDefinitions.FromSqlInterpolated($"""
                SELECT r.* FROM security.role_definitions AS r WHERE r."Id" = {command.RoleId} FOR SHARE OF r
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (subject.UserId is { } targetUser)
                await dbContext.Users.FromSqlInterpolated($"""
                    SELECT u.* FROM security.users AS u WHERE u."Id" = {targetUser} FOR SHARE OF u
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
            nowUtc = (clock ?? TimeProvider.System).GetUtcNow();
        }

        var targetActive = subject.Kind == AccessGrantSubjectKind.Group ||
            await dbContext.Users.AsNoTracking().AnyAsync(
                item => item.Id == subject.UserId && item.IsActive, cancellationToken);
        var admin = administrator is not null || await dbContext.Users.AsNoTracking()
            .AnyAsync(item => item.Id == actor.UserId && item.IsActive,
                cancellationToken);
        var isAdmin = administrator is not null || await dbContext.UserRoles.AsNoTracking()
            .AnyAsync(item => item.UserId == actor.UserId && item.Role == "Admin",
                cancellationToken);
        var role = await dbContext.RoleDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.RoleId && item.IsActive,
                cancellationToken);

        if (!targetActive || !admin || !isAdmin || role is null ||
            actor.UserId == subject.UserId)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_FORBIDDEN",
                FailureKind.Authorization,
                "Grant assignment requires an active user, role, and distinct administrator.");
        }

        if (command.ExpectedRoleRevision < 1 || role.Revision != command.ExpectedRoleRevision)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_ROLE_CONFLICT", FailureKind.Conflict,
                "Role changed since review. Reload and review its permissions again.");
        }

        await RequireAdministrationAsync(administrator, actor, cancellationToken);
        if (subject.GroupId is { } groupId)
            SecurityActorGroupGuard.RequireOtherGroup(actor, groupId, nowUtc, subject.DirectoryId!);

        var scopeIds = command.AssetIds.Distinct().ToArray();
        if (!Enum.IsDefined(command.ScopeKind) || scopeIds.Length != command.AssetIds.Count ||
            (command.ScopeKind == AccessGrantScopeKind.ExactAssets &&
             scopeIds.Length == 0) ||
            (command.ScopeKind != AccessGrantScopeKind.ExactAssets &&
             scopeIds.Length != 0) ||
            scopeIds.Length > ScopedGrantEvaluator.MaximumExactAssetsPerGrant ||
            scopeIds.Any(item => item == Guid.Empty))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_SCOPE_INVALID",
                FailureKind.Validation,
                "Invalid grant Asset scope.");
        }

        if (scopeIds.Length > 0)
        {
            if (administrator is not null)
                await dbContext.Assets.FromSqlInterpolated($"""
                    SELECT a.* FROM assets.assets AS a WHERE a."Id" = ANY({scopeIds}) ORDER BY a."Id" FOR SHARE OF a
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
            var count = await dbContext.Assets.AsNoTracking()
                .CountAsync(item => scopeIds.Contains(item.Id) && item.IsActive,
                    cancellationToken);
            if (count != scopeIds.Length)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_GRANT_ASSET_INACTIVE",
                    FailureKind.Validation,
                    "All granted Assets must exist and be active.");
            }
        }

        // Recheck role permission/scope compatibility transactionally. This
        // protects against concurrent role deactivation or content changes.
        if (administrator is not null)
            await dbContext.RolePermissions.FromSqlInterpolated($"""
                SELECT p.* FROM security.role_permissions AS p WHERE p."RoleId" = {command.RoleId} ORDER BY p."Permission" FOR SHARE OF p
                """).AsNoTracking().ToArrayAsync(cancellationToken);
        var scopes = await dbContext.RolePermissions.AsNoTracking()
            .Where(item => item.RoleId == command.RoleId)
            .Select(item => item.Permission)
            .ToArrayAsync(cancellationToken);
        var requiredScope = command.ScopeKind == AccessGrantScopeKind.DeploymentWide
            ? "DeploymentWide" : "ResourceScoped";
        if (!scopes.Any(permission => ProductPermissionCatalog.Definitions.Any(
            p => p.Key == permission && p.ScopeKind == requiredScope)))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_SCOPE_INVALID",
                FailureKind.Validation,
                "The selected role has no permissions for that scope.");
        }

        // Prevent uncontrolled duplicate grants; administrators should edit
        // the existing assignment rather than create indistinguishable rows.
        var existing = await dbContext.AccessGrants.AsNoTracking()
            .Where(item => item.SubjectKind == subject.Kind.ToString() && item.UserId == subject.UserId && item.GroupId == subject.GroupId && item.DirectoryId == subject.DirectoryId &&
                item.RoleId == command.RoleId &&
                item.ScopeKind == command.ScopeKind.ToString() && item.IsActive)
            .AnyAsync(cancellationToken);
        if (existing)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT",
                FailureKind.Conflict,
                "An active assignment for this role and scope already exists.");
        }

        var id = Guid.NewGuid();
        dbContext.AccessGrants.Add(new AccessGrantEntity
        {
            Id = id,
            SubjectKind = subject.Kind.ToString(),
            UserId = subject.UserId,
            GroupId = subject.GroupId,
            DirectoryId = subject.DirectoryId,
            GroupDisplayName = subject.DisplayName,
            GroupDomain = subject.Domain,
            RoleId = role.Id,
            ScopeKind = command.ScopeKind.ToString(),
            IsActive = true,
            Revision = 1,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        });
        foreach (var assetId in scopeIds)
        {
            dbContext.AccessGrantAssets.Add(new AccessGrantAssetEntity
            {
                GrantId = id,
                AssetId = assetId
            });
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = nowUtc,
            ActorUserId = actor.UserId,
            ActorIdentity = actor.IdentityName,
            Action = "security.grant.created",
            TargetType = "security-access-grant",
            TargetId = id.ToString("D"),
            CorrelationId = actor.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                subjectKind = subject.Kind.ToString(), userId = subject.UserId, groupId = subject.GroupId, directoryId = subject.DirectoryId, groupDisplayName = subject.DisplayName, groupDomain = subject.Domain, roleId = role.Id, roleRevision = role.Revision,
                scopeKind = command.ScopeKind.ToString(),
                assetIds = scopeIds,
                liveAuthorizationUnchanged = administrator is null
            }, AuditOptions)
        });

        try
        {
            await RequireAdministrationAsync(administrator, actor, cancellationToken);
            if (administrator is not null && subject.GroupId is { } checkedGroup)
                SecurityActorGroupGuard.RequireOtherGroup(actor, checkedGroup, (clock ?? TimeProvider.System).GetUtcNow(), subject.DirectoryId!);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrentWrite(exception))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "A concurrent security change occurred. Reload before retrying.",
                innerException: exception);
        }

        return new AccessGrant(id, subject.Kind,
            subject.UserId, subject.GroupId, role.Id, command.ScopeKind, scopeIds, true, 1) { DirectoryId = subject.DirectoryId, GroupDisplayName = subject.DisplayName, GroupDomain = subject.Domain };
    }

    public Task<AccessGrant> DeactivateAsync(
        AccessGrantSubject subject, Guid grantId, long expectedRevision,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        DeactivateCoreAsync(subject, grantId, expectedRevision, actor, nowUtc, null, cancellationToken);

    private async Task<AccessGrant> DeactivateCoreAsync(AccessGrantSubject subject, Guid grantId, long expectedRevision,
        SecurityActorContext actor, DateTimeOffset nowUtc, AccessSubject? administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        nowUtc = nowUtc.ToUniversalTime();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (administrator is not null)
        {
            await RequireAdministrationAsync(administrator, actor, cancellationToken);
            nowUtc = (clock ?? TimeProvider.System).GetUtcNow();
        }
        var authorized = actor.UserId is { } adminId && adminId != subject.UserId &&
            (administrator is not null || (
            await dbContext.Users.AsNoTracking().AnyAsync(
                user => user.Id == adminId && user.IsActive, cancellationToken) &&
            await dbContext.UserRoles.AsNoTracking().AnyAsync(
                role => role.UserId == adminId && role.Role == "Admin",
                cancellationToken)));
        if (!authorized)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active, distinct administrator is required.");
        }
        await RequireAdministrationAsync(administrator, actor, cancellationToken);
        if (subject.GroupId is { } groupId)
            SecurityActorGroupGuard.RequireOtherGroup(actor, groupId, nowUtc, subject.DirectoryId!);
        if (administrator is not null)
        {
            if (subject.UserId is { } targetUser)
                await dbContext.Users.FromSqlInterpolated($"""
                    SELECT u.* FROM security.users AS u WHERE u."Id" = {targetUser} FOR SHARE OF u
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
            await dbContext.AccessGrants.FromSqlInterpolated($"""
                SELECT g.* FROM security.access_grants AS g WHERE g."Id" = {grantId} FOR UPDATE OF g
                """).AsNoTracking().ToArrayAsync(cancellationToken);
        }
        if (subject.UserId is { } userId && !await dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId && user.IsActive, cancellationToken))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE", FailureKind.NotFound,
                "The target user is not active.");
        }
        var current = await dbContext.AccessGrants.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == grantId &&
                item.SubjectKind == subject.Kind.ToString() && item.UserId == subject.UserId && item.GroupId == subject.GroupId && item.DirectoryId == subject.DirectoryId,
                cancellationToken);
        if (current is null)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_NOT_FOUND", FailureKind.NotFound,
                "Assignment not found for the specified subject.");
        }
        if (!current.IsActive || current.Revision != expectedRevision)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Assignment revision changed. Reload before editing.");
        }

        try
        {
            // Recovery cannot depend on directory availability, custom roles,
            // legacy role claims or an Asset-scoped Admin assignment. Read the
            // complete direct recovery set inside this serializable transaction:
            // two writers retiring different final administrators must not both
            // commit. Keep the check before the mutation and its atomic audit.
            var recovery = DirectRecoveryAdministratorGrants();
            if (await recovery.AnyAsync(item => item.Id == grantId, cancellationToken) &&
                !await recovery.TagWith("Remaining direct recovery administrators")
                    .AnyAsync(item => item.Id != grantId, cancellationToken))
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_LAST_DIRECT_ADMIN_REQUIRED", FailureKind.Conflict,
                    "Assign another active user the built-in Admin role with deployment-wide scope before retiring the last direct administrator.");
            }

            // Atomic compare-and-set prevents stale writers and double audit.
            var changed = await dbContext.AccessGrants
                .Where(item => item.Id == grantId &&
                    item.SubjectKind == subject.Kind.ToString() && item.UserId == subject.UserId && item.GroupId == subject.GroupId && item.DirectoryId == subject.DirectoryId &&
                    item.IsActive && item.Revision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsActive, false)
                    .SetProperty(item => item.Revision, item => item.Revision + 1)
                    .SetProperty(item => item.UpdatedAtUtc, nowUtc),
                    cancellationToken);
            if (changed != 1)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                    "Assignment revision changed. Reload before editing.");
            }

            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = nowUtc,
                ActorUserId = actor.UserId,
                ActorIdentity = actor.IdentityName,
                Action = "security.grant.deactivated",
                TargetType = "security-access-grant",
                TargetId = grantId.ToString("D"),
                CorrelationId = actor.CorrelationId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    subjectKind = subject.Kind.ToString(), userId = subject.UserId, groupId = subject.GroupId, directoryId = subject.DirectoryId, groupDisplayName = current.GroupDisplayName, groupDomain = current.GroupDomain, roleId = current.RoleId,
                    scopeKind = current.ScopeKind,
                    previousRevision = expectedRevision,
                    newRevision = expectedRevision + 1,
                    liveAuthorizationUnchanged = administrator is null
                }, AuditOptions)
            });
            await RequireAdministrationAsync(administrator, actor, cancellationToken);
            if (administrator is not null && subject.GroupId is { } checkedGroup)
                SecurityActorGroupGuard.RequireOtherGroup(actor, checkedGroup, (clock ?? TimeProvider.System).GetUtcNow(), subject.DirectoryId!);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrentWrite(exception))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Concurrent security change. Reload before retrying.",
                innerException: exception);
        }

        var assets = await dbContext.AccessGrantAssets.AsNoTracking()
            .Where(item => item.GrantId == grantId)
            .OrderBy(item => item.AssetId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);
        return new AccessGrant(current.Id, subject.Kind,
            current.UserId, current.GroupId, current.RoleId,
            Enum.Parse<AccessGrantScopeKind>(current.ScopeKind),
            assets, false, expectedRevision + 1) { DirectoryId = current.DirectoryId, GroupDisplayName = current.GroupDisplayName, GroupDomain = current.GroupDomain };
    }

    public Task<IReadOnlyCollection<AccessGrant>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ListAsync(AccessGrantSubject.ForUser(userId), cancellationToken);

    private IQueryable<AccessGrantEntity> DirectRecoveryAdministratorGrants() =>
        dbContext.AccessGrants.AsNoTracking().Where(grant =>
            grant.Id != Guid.Empty && grant.IsActive && grant.Revision >= 1 &&
            grant.SubjectKind == "User" && grant.UserId != null &&
            grant.UserId != Guid.Empty && grant.GroupId == null &&
            grant.ScopeKind == "DeploymentWide" &&
            !dbContext.AccessGrantAssets.Any(binding => binding.GrantId == grant.Id) &&
            dbContext.Users.Any(user => user.Id == grant.UserId && user.IsActive) &&
            dbContext.RoleDefinitions.Any(role => role.Id == grant.RoleId &&
                role.IsSystem && role.Key == "admin" && role.IsActive && role.Revision >= 1 &&
                dbContext.RolePermissions.Any(permission => permission.RoleId == role.Id &&
                    permission.Permission == WellSitePermissions.SecurityManage)));

    public Task<AccessGrant> CreateForUserAsync(Guid userId, CreateAccessGrantCommand command, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        CreateAsync(AccessGrantSubject.ForUser(userId), command, actor, nowUtc, cancellationToken);

    public Task<AccessGrant> DeactivateForUserAsync(Guid userId, Guid grantId, long expectedRevision, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        DeactivateAsync(AccessGrantSubject.ForUser(userId), grantId, expectedRevision, actor, nowUtc, cancellationToken);

    private Task RequireAdministrationAsync(AccessSubject? administrator, SecurityActorContext actor, CancellationToken cancellationToken) =>
        administrator is null ? LegacyAdministratorWriteGuard.RequireAsync(dbContext, actor, cancellationToken)
            : GrantWriteGuard.RequireAdministratorAsync(dbContext, administrator, clock ?? TimeProvider.System, cancellationToken);

    // SaveChanges may wrap a PostgreSQL serialization/deadlock error first in
    // DbUpdateException, then in Npgsql's transient InvalidOperationException.
    // Classify the original provider code at any depth; never retry a security
    // mutation or turn unrelated failures (e.g. an invalid audit) into success.
    private static bool IsConcurrentWrite(Exception exception) =>
        exception.GetBaseException() is PostgresException postgres &&
        postgres.SqlState is PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation;

    private static AccessGrant ToDomain(AccessGrantEntity row,
        IEnumerable<Guid> assetIds)
    {
        if (!Enum.TryParse<AccessGrantSubjectKind>(row.SubjectKind, out var subject) ||
            !Enum.TryParse<AccessGrantScopeKind>(row.ScopeKind, out var scope))
        {
            throw new InvalidOperationException("Unsupported persisted grant type.");
        }

        return new AccessGrant(row.Id, subject, row.UserId, row.GroupId,
            row.RoleId, scope, assetIds.ToArray(), row.IsActive, row.Revision) { DirectoryId = row.DirectoryId, GroupDisplayName = row.GroupDisplayName, GroupDomain = row.GroupDomain };
    }
}
