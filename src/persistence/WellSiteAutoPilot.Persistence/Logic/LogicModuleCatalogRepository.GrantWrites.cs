using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed partial class LogicModuleCatalogRepository
{
    public async Task<InstalledLogicModule> RegisterAuthorizedAsync(InstalledLogicModule logicModule, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logicModule);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || subject.UserId == Guid.Empty || string.IsNullOrWhiteSpace(actor.IdentityName) ||
            actor.IdentityName.Length > 256 || actor.IdentityName.Any(char.IsControl) ||
            (actor.CorrelationId is { } correlation && (string.IsNullOrWhiteSpace(correlation) || correlation.Length > 128 || correlation.Any(char.IsControl))))
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
        if (logicModule.Id == Guid.Empty || !Enum.IsDefined(logicModule.TrustStatus) ||
            logicModule.IsEnabled != (logicModule.TrustStatus is LogicModuleTrustStatus.TrustedPublisher or LogicModuleTrustStatus.CustomerApproved))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Module trust metadata is invalid.");
        var time = clock ?? TimeProvider.System;
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.LogicManage, time, cancellationToken);
            var existing = await dbContext.LogicModules.FromSqlInterpolated($"""
                SELECT m.* FROM logic.logic_modules AS m
                WHERE m."ModuleId" = {logicModule.ModuleId} AND m."Version" = {logicModule.Version} FOR SHARE OF m
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            // The catalog lookup can wait on another writer. Retain and
            // recheck current global authority even for an idempotent result.
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.LogicManage, time, cancellationToken);
            if (existing.Length == 1)
            {
                var current = ToDomain(existing[0]);
                if (!string.Equals(current.PackageSha256, logicModule.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
                    current.ManifestJson != logicModule.ManifestJson)
                    throw new WellSiteAutoPilotException("MODULE_VERSION_IMMUTABLE", FailureKind.Conflict,
                        "This module version is already installed with different content.");
                // Registration never changes an installed version's approval
                // or retirement state, even if its caller proposes a different
                // initial trust classification for the same immutable content.
                await transaction.CommitAsync(cancellationToken);
                return current;
            }
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = time.GetUtcNow(), ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId,
                Action = "logic-module.registered", TargetType = "logic-module", TargetId = logicModule.Id.ToString("D"),
                DetailsJson = JsonSerializer.Serialize(new { logicModule.ModuleId, logicModule.Version, logicModule.PackageSha256, TrustStatus = logicModule.TrustStatus.ToString() })
            });
            await AddAsync(logicModule, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return logicModule;
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException("MODULE_CATALOG_CONFLICT", FailureKind.Conflict,
                "The catalog or its authority changed concurrently. Reload before retrying.", innerException: exception);
        }
    }
}
