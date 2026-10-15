using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>Shared storage mechanics; callers retain distinct typed diagnostic contracts.</summary>
internal static class SecurityDiagnosticAuditWriter
{
    private static readonly JsonSerializerOptions AuditOptions = new(JsonSerializerDefaults.Web);

    public static async Task RecordAsync(
        WellSiteAutoPilotDbContext db, SecurityActorContext actor, DateTimeOffset nowUtc,
        string action, string targetType, string targetId, Guid? assetId, object details,
        CancellationToken cancellationToken)
    {
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
                "Only a current active administrator can inspect security diagnostics.");
        }
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = nowUtc.ToUniversalTime(),
            ActorUserId = administratorId, ActorIdentity = actor.IdentityName,
            Action = action, TargetType = targetType, TargetId = targetId, AssetId = assetId,
            CorrelationId = actor.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(details, AuditOptions)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
