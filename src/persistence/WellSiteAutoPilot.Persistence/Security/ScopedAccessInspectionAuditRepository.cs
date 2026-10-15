using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class ScopedAccessInspectionAuditRepository(WellSiteAutoPilotDbContext db)
    : IScopedAccessInspectionAuditRepository
{
    private static readonly JsonSerializerOptions AuditOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        ScopedAccessInspection inspection, SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(actor);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (actor.UserId is not { } administratorId || administratorId == Guid.Empty ||
            !await db.Users.AsNoTracking().AnyAsync(user => user.Id == administratorId && user.IsActive,
                cancellationToken) ||
            !await db.UserRoles.AsNoTracking().AnyAsync(role => role.UserId == administratorId && role.Role == "Admin",
                cancellationToken))
        {
            throw new WellSiteAutoPilotException("SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "Only a current active administrator can inspect effective access.");
        }

        // Keep stable codes and counts, not directory contents, role/grant
        // payloads, Asset display names or any customer operational data.
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = nowUtc.ToUniversalTime(),
            ActorUserId = administratorId, ActorIdentity = actor.IdentityName,
            Action = "security.access.inspected", TargetType = "security-user",
            TargetId = inspection.UserId.ToString("D"), AssetId = inspection.AssetId,
            CorrelationId = actor.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                permissionCodes = inspection.Permissions.Select(item => item.Permission).ToArray(),
                inspection.WideningCount, inspection.ReductionCount,
                diagnosticOnly = true, liveAuthorizationUnchanged = true
            }, AuditOptions)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
