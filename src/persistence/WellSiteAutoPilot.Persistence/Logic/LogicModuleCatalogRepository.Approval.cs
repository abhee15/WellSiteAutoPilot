using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Messaging;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed partial class LogicModuleCatalogRepository : ILogicModuleApprovalRepository
{
    public async Task<InstalledLogicModule> ChangeApprovalAuthorizedAsync(
        string moduleId, string version, LogicModuleApprovalAction action, string reason,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        RequireBoundActor(subject, actor);
        var time = clock ?? TimeProvider.System;
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.LogicManage, time, cancellationToken);
            // Anything that depends on this module holds the same row FOR SHARE
            // while it saves, so a change here waits for it and is seen by the next.
            var rows = await dbContext.LogicModules.FromSqlInterpolated($"""
                SELECT m.* FROM logic.logic_modules AS m
                WHERE m."ModuleId" = {moduleId} AND m."Version" = {version} FOR UPDATE OF m
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            // The row lock can wait on another writer; recheck current authority.
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.LogicManage, time, cancellationToken);
            if (rows.Length != 1)
                throw new WellSiteAutoPilotException("MODULE_NOT_INSTALLED", FailureKind.NotFound,
                    $"Logic Module '{moduleId}' version '{version}' is not installed.");
            var current = ToDomain(rows[0]);
            var change = LogicModuleApproval.Decide(current, action);
            if (change is null)
            {
                // Upgrade compatibility: an older Server could approve stored
                // bytes before publication existed. Repeat approval may repair
                // the missing delivery request without changing trust/history.
                if (action == LogicModuleApprovalAction.Approve && current.IsEnabled &&
                    (current.TrustStatus is LogicModuleTrustStatus.CustomerApproved or LogicModuleTrustStatus.TrustedPublisher) &&
                    await QueuePackagePublicationAsync(current, actor, time.GetUtcNow(), cancellationToken))
                {
                    dbContext.AuditEvents.Add(new AuditEventEntity
                    {
                        Id = Guid.NewGuid(), OccurredAtUtc = time.GetUtcNow(), ActorUserId = subject.UserId,
                        ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId,
                        Action = "logic-module.package-publication-queued", TargetType = "logic-module", TargetId = current.Id.ToString("D"),
                        DetailsJson = JsonSerializer.Serialize(new { current.ModuleId, current.Version, current.PackageSha256 })
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                return current;
            }

            var now = time.GetUtcNow();
            var newStatus = change.TrustStatus.ToString();
            var newEnabled = change.IsEnabled;
            var changed = await dbContext.LogicModules.Where(item => item.Id == current.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.TrustStatus, newStatus)
                    .SetProperty(item => item.IsEnabled, newEnabled), cancellationToken);
            if (changed != 1)
                throw new WellSiteAutoPilotException("MODULE_CATALOG_CONFLICT", FailureKind.Conflict,
                    "The catalog changed concurrently. Reload before retrying.");
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = now, ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId,
                Action = action == LogicModuleApprovalAction.Approve ? "logic-module.approved" : "logic-module.retired",
                TargetType = "logic-module", TargetId = current.Id.ToString("D"),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    current.ModuleId, current.Version, current.PackageSha256,
                    PreviousTrustStatus = current.TrustStatus.ToString(), PreviousIsEnabled = current.IsEnabled,
                    TrustStatus = change.TrustStatus.ToString(), change.IsEnabled, Reason = reason
                })
            });
            if (action == LogicModuleApprovalAction.Approve)
                await QueuePackagePublicationAsync(current, actor, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return current with { TrustStatus = change.TrustStatus, IsEnabled = change.IsEnabled };
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException("MODULE_CATALOG_CONFLICT", FailureKind.Conflict,
                "The catalog or its authority changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private async Task<bool> QueuePackagePublicationAsync(InstalledLogicModule current, SecurityActorContext actor,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await dbContext.LogicModulePackages.AnyAsync(item => item.Sha256 == current.PackageSha256, cancellationToken) ||
            await dbContext.OutboxMessages.AnyAsync(item => item.Id == current.Id, cancellationToken)) return false;
        // One durable publication identity per immutable module catalog version.
        dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = current.Id, Type = ModulePackagePublication.OutboxType, CreatedAtUtc = now,
            Payload = JsonSerializer.Serialize(new ModulePackagePublication(current.Id, current.ModuleId,
                current.Version, current.PackageSha256, actor.CorrelationId, Activity.Current?.Id))
        });
        return true;
    }

    private static void RequireBoundActor(AccessSubject subject, SecurityActorContext actor)
    {
        if (actor.UserId != subject.UserId || subject.UserId == Guid.Empty || string.IsNullOrWhiteSpace(actor.IdentityName) ||
            actor.IdentityName.Length > 256 || actor.IdentityName.Any(char.IsControl) ||
            (actor.CorrelationId is { } correlation && (string.IsNullOrWhiteSpace(correlation) || correlation.Length > 128 || correlation.Any(char.IsControl))))
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
    }
}
