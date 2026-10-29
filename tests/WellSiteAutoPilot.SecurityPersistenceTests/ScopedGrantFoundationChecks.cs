using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class ScopedGrantFoundationChecks
{
    public static async Task RunAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options,
        Guid userId, Guid roleId, Guid assetId)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        if (db.Database.HasPendingModelChanges())
        {
            throw new InvalidOperationException("Scoped grant model and migration snapshot differ.");
        }

        var legacyRoles = await db.UserRoles.AsNoTracking()
            .Where(item => item.UserId == userId).ToArrayAsync();
        var legacyScopes = await db.UserAssetScopes.AsNoTracking()
            .Where(item => item.UserId == userId).ToArrayAsync();
        var originalGrants = await db.AccessGrants.CountAsync();

        // Real PostgreSQL constraints, with no lasting security test assignments.
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var exact = Grant();
            db.AccessGrants.AddRange(exact,
                Grant(scope: "AllAssets"), Grant(scope: "DeploymentWide"),
                Grant(active: false), Grant(subject: "Group", groupId: "S-1-5-21-100"));
            db.AccessGrantAssets.Add(new AccessGrantAssetEntity
            {
                GrantId = exact.Id, AssetId = assetId
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await RejectAsync(Grant(), "23505", "IX_access_grants_UserId_RoleId_ScopeKind");
            await RejectAsync(Grant(subject: "Unknown"), "23514", "CK_access_grants_subject");
            await RejectAsync(Grant(subject: "Group"), "23514", "CK_access_grants_subject");
            await RejectAsync(Grant(groupId: "S-1-5-21-100"), "23514", "CK_access_grants_subject");
            await RejectAsync(Grant(scope: "Unknown"), "23514", "CK_access_grants_scope_kind");
            await RejectAsync(Grant(revision: 0, active: false), "23514", "CK_access_grants_revision");
            await RejectAsync(Grant(user: Guid.NewGuid()), "23503", "FK_access_grants_users_UserId");
            await RejectAsync(Grant(role: Guid.NewGuid()), "23503", "FK_access_grants_role_definitions_RoleId");
            await RejectAsync(new AccessGrantAssetEntity
            {
                GrantId = exact.Id, AssetId = Guid.NewGuid()
            }, "23503", "FK_assignment_asset_scopes_assets_AssetId");
            await RejectAsync(new AccessGrantAssetEntity
            {
                GrantId = Guid.NewGuid(), AssetId = assetId
            }, "23503", "FK_assignment_asset_scopes_access_grants_GrantId");
            await RejectAsync(new AccessGrantAssetEntity
            {
                GrantId = exact.Id, AssetId = assetId
            }, "23505", "PK_assignment_asset_scopes");

            // Retired assignments may coexist with an active assignment; grant
            // cleanup cascades only to its own scope rows, never to an Asset.
            db.AccessGrants.Remove(exact);
            await db.SaveChangesAsync();
            if (await db.AccessGrantAssets.AnyAsync(item => item.GrantId == exact.Id) ||
                !await db.Assets.AnyAsync(item => item.Id == assetId))
            {
                throw new InvalidOperationException("Grant deletion damaged Asset or scope integrity.");
            }

            if (!legacyRoles.Select(item => item.Role).Order().SequenceEqual(
                    await db.UserRoles.Where(item => item.UserId == userId)
                        .OrderBy(item => item.Role).Select(item => item.Role).ToArrayAsync()) ||
                !legacyScopes.Select(item => item.AssetId).Order().SequenceEqual(
                    await db.UserAssetScopes.Where(item => item.UserId == userId)
                        .OrderBy(item => item.AssetId).Select(item => item.AssetId).ToArrayAsync()))
            {
                throw new InvalidOperationException("Grant storage changed legacy authorization assignments.");
            }
            await transaction.RollbackAsync();
        }

        if (await db.AccessGrants.CountAsync() != originalGrants)
        {
            throw new InvalidOperationException("Foundation checks left test grants behind.");
        }

        AccessGrantEntity Grant(string subject = "User", string scope = "ExactAssets",
            bool active = true, long revision = 1, string? groupId = null,
            Guid? user = null, Guid? role = null) => new()
        {
            Id = Guid.NewGuid(), SubjectKind = subject,
            UserId = subject == "Group" ? null : user ?? userId,
            GroupId = groupId, RoleId = role ?? roleId, ScopeKind = scope,
            IsActive = active, Revision = revision,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        async Task RejectAsync(object entity, string sqlState, string constraint)
        {
            db.Add(entity);
            try
            {
                await db.SaveChangesAsync();
                throw new InvalidOperationException($"PostgreSQL accepted invalid grant: {constraint}.");
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException postgres &&
                    postgres.SqlState == sqlState && postgres.ConstraintName == constraint)
            {
                // EF restores its savepoint before the next negative case.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
