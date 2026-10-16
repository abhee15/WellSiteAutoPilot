using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// One-time initialization of a fresh deployment. No legacy role writes and no
/// automatic regrant after retirement/deactivation. The audit is also a durable
/// initialization marker if grants are subsequently removed outside the API.
/// </summary>
public sealed class GrantBootstrapRepository(WellSiteAutoPilotDbContext db) : IGrantBootstrapRepository
{
    public const string AuditAction = "security.bootstrap-grants.assigned";

    public async Task<bool> TryInitializeAdministratorAsync(Guid userId, DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || await AlreadyInitializedAsync(cancellationToken)) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Same cross-instance initialization lock as the current bootstrap;
        // no migration or authorization mode state is introduced.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2147483000, 1)", cancellationToken);
        if (await AlreadyInitializedAsync(cancellationToken)) return false;
        var users = await db.Users.FromSqlInterpolated($"""
            SELECT u.* FROM security.users AS u WHERE u."Id" = {userId} AND u."IsActive" = TRUE
            FOR SHARE OF u
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (users.Length != 1) return false;
        var roles = await db.RoleDefinitions.FromSqlRaw("""
            SELECT r.* FROM security.role_definitions AS r
            WHERE r."Key" = 'admin' AND r."IsSystem" = TRUE AND r."IsActive" = TRUE AND r."Revision" >= 1
            FOR SHARE OF r
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (roles.Length != 1) throw InvalidRole();
        var roleId = roles[0].Id;
        var permissions = await db.RolePermissions.FromSqlInterpolated($"""
            SELECT p.* FROM security.role_permissions AS p WHERE p."RoleId" = {roleId} FOR SHARE OF p
            """).AsNoTracking().Select(item => item.Permission).ToArrayAsync(cancellationToken);
        if (!permissions.ToHashSet(StringComparer.Ordinal).SetEquals(WellSitePermissions.All)) throw InvalidRole();

        var now = occurredAtUtc.ToUniversalTime();
        var deployment = NewGrant("DeploymentWide");
        var assets = NewGrant("AllAssets");
        db.AccessGrants.AddRange(deployment, assets);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = now, ActorIdentity = "system:bootstrap",
            Action = AuditAction, TargetType = "security-user", TargetId = userId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new
            {
                UserId = userId, RoleId = roleId, Origin = "bootstrap",
                DeploymentGrantId = deployment.Id, AssetGrantId = assets.Id
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;

        AccessGrantEntity NewGrant(string scope) => new()
        {
            Id = Guid.NewGuid(), SubjectKind = "User", UserId = userId, RoleId = roleId,
            ScopeKind = scope, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        };
    }

    private async Task<bool> AlreadyInitializedAsync(CancellationToken cancellationToken) =>
        await db.AccessGrants.AnyAsync(cancellationToken) ||
        await db.AuditEvents.AnyAsync(audit => audit.Action == AuditAction, cancellationToken);

    private static WellSiteAutoPilotException InvalidRole() => new("SECURITY_BOOTSTRAP_ROLE_INVALID",
        FailureKind.Conflict, "The protected administrator role is unavailable or invalid. Repair the deployment before initialization.");
}
