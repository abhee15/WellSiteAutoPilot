using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Paired-grant mutation fence. The caller owns the transaction, resolves and
/// holds persisted business targets, writes its mutation/audit, then commits.
/// Checking this outside that transaction is deliberately unsupported.
/// </summary>
public static class GrantWriteGuard
{
    public static Task RequireAdministratorAsync(WellSiteAutoPilotDbContext db, AccessSubject subject,
        TimeProvider clock, CancellationToken cancellationToken = default) =>
        RequireAsync(db, subject, WellSitePermissions.SecurityManage, null, clock, true, cancellationToken);

    public static Task RequireAllAssetsAsync(WellSiteAutoPilotDbContext db, AccessSubject subject,
        string permission, TimeProvider clock, CancellationToken cancellationToken = default) =>
        RequireAsync(db, subject, permission, [], clock, false, cancellationToken, requireAllAssets: true);

    public static Task RequireDeploymentAsync(WellSiteAutoPilotDbContext db, AccessSubject subject,
        string permission, TimeProvider clock, CancellationToken cancellationToken = default) =>
        RequireAsync(db, subject, permission, null, clock, permission == WellSitePermissions.SecurityManage, cancellationToken);

    public static Task RequireAssetsAsync(WellSiteAutoPilotDbContext db, AccessSubject subject,
        string permission, IReadOnlyCollection<Guid> assetIds, TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count == 0 || assetIds.Contains(Guid.Empty)) throw Denied();
        return RequireAsync(db, subject, permission, assetIds.Distinct().Order().ToArray(), clock, false, cancellationToken);
    }

    private static async Task RequireAsync(WellSiteAutoPilotDbContext db, AccessSubject subject,
        string permission, Guid[]? targets, TimeProvider clock, bool protectedAdministrator, CancellationToken cancellationToken,
        bool requireAllAssets = false)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(clock);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Grant mutation authorization requires a transaction.");
        try
        {
            var now = clock.GetUtcNow();
            var grants = targets is null
                ? ScopedAccessQueries.DeploymentGrants(db, subject.UserId, permission, subject.Groups, now)
                : ScopedAccessQueries.ResourceGrants(db, subject.UserId, permission, subject.Groups, now);
            if (protectedAdministrator)
                grants = ScopedAccessQueries.AdministratorGrants(db, subject.UserId, subject.Groups, now);
            if (requireAllAssets) grants = grants.Where(grant => grant.ScopeKind == "AllAssets");
            var selected = new HashSet<Guid>();
            if (targets is null || requireAllAssets)
            {
                var id = await grants.OrderBy(grant => grant.Id).Select(grant => grant.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (id == Guid.Empty) throw Denied();
                selected.Add(id);
            }
            else
            {
                // A qualifying explicit AllAssets grant covers the entire set. Retain and recheck it below,
                // just like a chosen exact grant, without one lookup per well in a large View assignment.
                var allAssetsGrant = await grants.Where(grant => grant.ScopeKind == "AllAssets")
                    .OrderBy(grant => grant.Id).Select(grant => grant.Id).FirstOrDefaultAsync(cancellationToken);
                if (allAssetsGrant != Guid.Empty) selected.Add(allAssetsGrant);
                foreach (var target in allAssetsGrant == Guid.Empty ? targets : [])
                {
                    var id = await grants.Where(grant => grant.ScopeKind == "AllAssets" ||
                            db.AccessGrantAssets.Any(binding => binding.GrantId == grant.Id && binding.AssetId == target))
                        .OrderBy(grant => grant.Id).Select(grant => grant.Id).FirstOrDefaultAsync(cancellationToken);
                    if (id == Guid.Empty) throw Denied();
                    selected.Add(id);
                }
            }

            var userId = subject.UserId;
            foreach (var id in selected.Order())
            {
                // UPDATE on the grant also prevents new FK bindings being
                // inserted until commit. SHARE retains active user/role and
                // the exact permission row, including against direct deletion.
                var held = await db.AccessGrants.FromSqlInterpolated($"""
                    SELECT g.* FROM security.access_grants AS g
                    INNER JOIN security.users AS u ON u."Id" = {userId} AND u."IsActive" = TRUE
                    INNER JOIN security.role_definitions AS r ON r."Id" = g."RoleId" AND r."IsActive" = TRUE AND r."Revision" >= 1
                    INNER JOIN security.role_permissions AS p ON p."RoleId" = r."Id" AND p."Permission" = {permission}
                    WHERE g."Id" = {id} AND g."IsActive" = TRUE AND g."Revision" >= 1
                    FOR UPDATE OF g FOR SHARE OF u, r, p
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
                if (held.Length != 1) throw Denied();
            }
            var ids = selected.Order().ToArray();
            // Exact bindings are immutable through administration APIs. Hold
            // existing rows as well so direct deletion cannot invalidate a
            // complete grant while this mutation is in flight.
            await db.AccessGrantAssets.FromSqlInterpolated($"""
                SELECT b.* FROM security.assignment_asset_scopes AS b
                WHERE b."GrantId" = ANY({ids}) ORDER BY b."GrantId", b."AssetId" FOR SHARE OF b
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (targets is { Length: > 0 })
            {
                await db.Assets.FromSqlInterpolated($"""
                    SELECT a.* FROM assets.assets AS a
                    WHERE a."Id" = ANY({targets}) OR EXISTS (
                        SELECT 1 FROM security.assignment_asset_scopes AS b
                        WHERE b."GrantId" = ANY({ids}) AND b."AssetId" = a."Id")
                    ORDER BY a."Id" FOR SHARE OF a
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
            }

            // Re-evaluate after every potentially blocking lock, using fresh
            // time and only the grants we actually hold. Never substitute a
            // newly added but unlocked assignment after a concurrent revoke.
            now = clock.GetUtcNow();
            if (requireAllAssets)
            {
                if (!await ScopedAccessQueries.ResourceGrants(db, userId, permission, subject.Groups, now)
                        .AnyAsync(grant => ids.Contains(grant.Id) && grant.ScopeKind == "AllAssets", cancellationToken)) throw Denied();
            }
            else if (targets is null)
            {
                var heldGrants = protectedAdministrator
                    ? ScopedAccessQueries.AdministratorGrants(db, userId, subject.Groups, now)
                    : ScopedAccessQueries.DeploymentGrants(db, userId, permission, subject.Groups, now);
                if (!await heldGrants
                        .AnyAsync(grant => ids.Contains(grant.Id), cancellationToken)) throw Denied();
            }
            else
            {
                var heldGrants = ScopedAccessQueries.ResourceGrants(db, userId, permission, subject.Groups, now)
                    .Where(grant => ids.Contains(grant.Id));
                var count = await db.Assets.CountAsync(asset => targets.Contains(asset.Id) && asset.IsActive &&
                    heldGrants.Any(grant => grant.ScopeKind == "AllAssets" ||
                        db.AccessGrantAssets.Any(binding => binding.GrantId == grant.Id && binding.AssetId == asset.Id)),
                    cancellationToken);
                if (count != targets.Length) throw Denied();
            }
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException("SECURITY_ACCESS_CONFLICT", FailureKind.Conflict,
                "Access changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private static WellSiteAutoPilotException Denied() => new("SECURITY_SCOPE_DENIED",
        FailureKind.Authorization, "The current user is not authorized for this operation.");
}
