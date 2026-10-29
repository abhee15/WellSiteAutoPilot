using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>Role definitions are not active grants until scoped entitlement cutover.</summary>
public sealed class RoleCatalogRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null)
    : IRoleCatalogRepository
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<RoleDefinition>> ListAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.RoleDefinitions.AsNoTracking()
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Take(limit).ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return [];
        }

        var ids = rows.Select(item => item.Id).ToArray();
        var permissions = await dbContext.RolePermissions.AsNoTracking()
            .Where(item => ids.Contains(item.RoleId))
            .ToArrayAsync(cancellationToken);

        return rows.Select(item => ToDomain(item,
            permissions.Where(permission => permission.RoleId == item.Id)
                .Select(permission => permission.Permission))).ToArray();
    }

    public async Task<RoleDefinition?> GetAsync(
        Guid roleId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.RoleDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var permissions = await dbContext.RolePermissions.AsNoTracking()
            .Where(item => item.RoleId == roleId)
            .Select(item => item.Permission).ToArrayAsync(cancellationToken);

        return ToDomain(row, permissions);
    }

    public Task<RoleDefinition> CreateAuthorizedAsync(RoleDefinition role, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default) => GrantAdministrationActor.ExecuteAsync(() =>
            CreateCoreAsync(role, GrantAdministrationActor.Bind(subject, actor), subject, cancellationToken));

    public Task<RoleDefinition> UpdateAuthorizedAsync(Guid roleId, long expectedRevision, RoleMutationCommand mutation,
        AccessSubject subject, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        GrantAdministrationActor.ExecuteAsync(() => UpdateCoreAsync(roleId, expectedRevision, mutation,
            GrantAdministrationActor.Bind(subject, actor), nowUtc, subject, cancellationToken));

    public Task<RoleDefinition> CreateAsync(
        RoleDefinition role, SecurityActorContext actor,
        CancellationToken cancellationToken = default) => CreateCoreAsync(role, actor, null, cancellationToken);

    private async Task<RoleDefinition> CreateCoreAsync(RoleDefinition role, SecurityActorContext actor,
        AccessSubject? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(actor);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdministrationAsync(subject, actor, cancellationToken);
        var normalizedName = NormalizeName(role.Name);

        dbContext.RoleDefinitions.Add(new RoleDefinitionEntity
        {
            Id = role.Id,
            Key = role.Key,
            Name = role.Name,
            NormalizedName = normalizedName,
            Description = role.Description,
            IsSystem = false,
            IsActive = role.IsActive,
            Revision = 1,
            CreatedAtUtc = role.CreatedAtUtc,
            UpdatedAtUtc = role.UpdatedAtUtc
        });

        foreach (var permission in role.Permissions)
        {
            dbContext.RolePermissions.Add(new RolePermissionEntity
            {
                RoleId = role.Id, Permission = permission
            });
        }

        dbContext.AuditEvents.Add(Audit(
            role.Id, actor, "security.role.created",
            role.CreatedAtUtc, new
            {
                role.Key, role.Name, role.IsActive,
                role.Permissions
            }));

        try
        {
            await RequireAdministrationAsync(subject, actor, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Conflict("Role name already exists.", exception);
        }

        return await GetAsync(role.Id, cancellationToken) ??
            throw new InvalidOperationException("Created role was not persisted.");
    }

    public Task<RoleDefinition> UpdateAsync(
        Guid roleId, long expectedRevision, RoleMutationCommand mutation,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        UpdateCoreAsync(roleId, expectedRevision, mutation, actor, nowUtc, null, cancellationToken);

    private async Task<RoleDefinition> UpdateCoreAsync(Guid roleId, long expectedRevision, RoleMutationCommand mutation,
        SecurityActorContext actor, DateTimeOffset nowUtc, AccessSubject? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(actor);

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await RequireAdministrationAsync(subject, actor, cancellationToken);
            // Prevent new FK-backed assignments while deciding whether this
            // role's permissions/state may change. Reads and updates share it.
            await dbContext.RoleDefinitions.FromSqlInterpolated($"""
                SELECT r.* FROM security.role_definitions AS r WHERE r."Id" = {roleId} FOR UPDATE OF r
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (subject is not null) nowUtc = (clock ?? TimeProvider.System).GetUtcNow();

            var current = await GetAsync(roleId, cancellationToken) ??
                throw new WellSiteAutoPilotException(
                    "SECURITY_ROLE_NOT_FOUND", FailureKind.NotFound,
                    "The requested role does not exist.");

            if (current.IsSystem)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_SYSTEM_ROLE_IMMUTABLE", FailureKind.Authorization,
                    "Built-in role definitions cannot be modified.");
            }

            if (current.Revision != expectedRevision)
            {
                throw Conflict("Role was modified; reload its latest revision.");
            }

            // Active grants reference a role, not a frozen permission revision.
            // Until revision-pinned grants exist, permission edits or role
            // reactivation could silently expand those assignments. Force
            // administrators to retire assignments before either change.
            var permissionChanged = !current.Permissions.ToHashSet(StringComparer.Ordinal)
                .SetEquals(mutation.Permissions);
            if (permissionChanged || (!current.IsActive && mutation.IsActive))
            {
                var assigned = await dbContext.AccessGrants.AsNoTracking()
                    .AnyAsync(item => item.RoleId == roleId && item.IsActive,
                        cancellationToken);
                if (assigned)
                {
                    throw new WellSiteAutoPilotException(
                        "SECURITY_ROLE_ASSIGNED", FailureKind.Conflict,
                        "Retire active grants before changing role permissions or reactivating the role.");
                }
            }

            if (current.IsActive && !mutation.IsActive)
            {
                var assignments = dbContext.AccessGrants.AsNoTracking()
                    .Where(grant => grant.RoleId == roleId && grant.IsActive);
                var ownDirect = await assignments.AnyAsync(grant =>
                    grant.SubjectKind == "User" && grant.UserId == actor.UserId,
                    cancellationToken);
                var ownGroup = false;
                if (await assignments.AnyAsync(grant => grant.SubjectKind == "Group", cancellationToken))
                {
                    var groups = SecurityActorGroupGuard.RequireUsableGroups(actor, nowUtc).ToArray();
                    var directoryId = actor.Groups?.DirectoryId;
                    ownGroup = await assignments.AnyAsync(grant =>
                        grant.SubjectKind == "Group" && grant.DirectoryId == directoryId && groups.Contains(grant.GroupId!),
                        cancellationToken);
                }
                if (ownDirect || ownGroup)
                    throw new WellSiteAutoPilotException("SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN",
                        FailureKind.Authorization, "Administrators may not deactivate a role assigned to themselves or their Windows groups.");
            }

            var changed = await dbContext.RoleDefinitions
                .Where(item => item.Id == roleId &&
                    !item.IsSystem && item.Revision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Name, mutation.Name)
                    .SetProperty(item => item.NormalizedName, NormalizeName(mutation.Name))
                    .SetProperty(item => item.Description, mutation.Description)
                    .SetProperty(item => item.IsActive, mutation.IsActive)
                    .SetProperty(item => item.Revision, item => item.Revision + 1)
                    .SetProperty(item => item.UpdatedAtUtc, nowUtc),
                    cancellationToken);

            if (changed != 1)
            {
                throw Conflict("Role was modified; reload its latest revision.");
            }

            await dbContext.RolePermissions
                .Where(item => item.RoleId == roleId)
                .ExecuteDeleteAsync(cancellationToken);

            // ExecuteDelete bypasses EF's state manager. Prior creation in
            // this scoped DbContext may still track keys that are about to be
            // re-inserted; detach only those rows to avoid false collisions.
            foreach (var tracked in dbContext.RolePermissions.Local
                .Where(item => item.RoleId == roleId).ToArray())
            {
                dbContext.Entry(tracked).State = EntityState.Detached;
            }

            foreach (var permission in mutation.Permissions)
            {
                dbContext.RolePermissions.Add(new RolePermissionEntity
                {
                    RoleId = roleId, Permission = permission
                });
            }

            dbContext.AuditEvents.Add(Audit(
                roleId, actor, "security.role.updated", nowUtc,
                new
                {
                    previousRevision = current.Revision,
                    previousName = current.Name,
                    previousPermissions = current.Permissions,
                    previousActive = current.IsActive,
                    newName = mutation.Name,
                    newPermissions = mutation.Permissions,
                    mutation.IsActive
                }));

            await RequireAdministrationAsync(subject, actor, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState is
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
        {
            throw Conflict("Role changed concurrently or its name already exists.", exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Conflict("Role name already exists.", exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is PostgresException postgres &&
                postgres.SqlState is
                    PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected)
        {
            // Npgsql's EF execution strategy can wrap a 40001/40P01 in a
            // transient-failure InvalidOperationException. Surface the
            // same revision conflict as a direct PostgreSQL exception:
            // never retry a stale administrator write automatically.
            throw Conflict(
                "Role changed concurrently; reload its latest revision.",
                exception);
        }

        return await GetAsync(roleId, cancellationToken) ??
            throw new InvalidOperationException("Updated role was not persisted.");
    }

    private static WellSiteAutoPilotException Conflict(
        string reason, Exception? inner = null) =>
        new("SECURITY_ROLE_CONFLICT", FailureKind.Conflict, reason,
            innerException: inner);

    private Task RequireAdministrationAsync(AccessSubject? subject, SecurityActorContext actor, CancellationToken cancellationToken) =>
        subject is null ? LegacyAdministratorWriteGuard.RequireAsync(dbContext, actor, cancellationToken)
            : GrantWriteGuard.RequireAdministratorAsync(dbContext, subject, clock ?? TimeProvider.System, cancellationToken);

    private static string NormalizeName(string name) =>
        name.Trim().ToUpperInvariant();

    private static RoleDefinition ToDomain(RoleDefinitionEntity item,
        IEnumerable<string> permissions) =>
        new(item.Id, item.Key, item.Name, item.Description, item.IsSystem,
            item.IsActive, item.Revision, item.CreatedAtUtc, item.UpdatedAtUtc,
            permissions.OrderBy(value => value, StringComparer.Ordinal).ToArray());

    private static AuditEventEntity Audit(
        Guid roleId, SecurityActorContext actor, string action,
        DateTimeOffset time, object details) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = time.ToUniversalTime(),
            ActorUserId = actor.UserId,
            ActorIdentity = actor.IdentityName,
            Action = action,
            TargetType = "security-role",
            TargetId = roleId.ToString("D"),
            CorrelationId = actor.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(details, AuditJsonOptions)
        };
}
