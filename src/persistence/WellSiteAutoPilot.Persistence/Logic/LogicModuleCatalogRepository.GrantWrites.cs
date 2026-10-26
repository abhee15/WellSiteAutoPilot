using System.Data;
using System.Security.Cryptography;
using WellSiteAutoPilot.Application.Logic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed partial class LogicModuleCatalogRepository : ILogicModulePackageRepository
{
    public Task<InstalledLogicModule> RegisterAuthorizedAsync(InstalledLogicModule logicModule, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        RegisterAuthorizedCoreAsync(logicModule, null, subject, actor, cancellationToken);

    public Task<InstalledLogicModule> UploadAuthorizedAsync(InstalledLogicModule logicModule, byte[] archive,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logicModule);
        ArgumentNullException.ThrowIfNull(archive);
        var snapshot = archive.ToArray();
        if (snapshot.Length is < 1 or > LogicModulePackageService.MaximumUploadBytes ||
            logicModule.TrustStatus != LogicModuleTrustStatus.Untrusted || logicModule.IsEnabled ||
            !string.Equals(Convert.ToHexStringLower(SHA256.HashData(snapshot)), logicModule.PackageSha256, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_INVALID", FailureKind.Validation,
                "Package content and initial trust metadata are invalid.");
        return RegisterAuthorizedCoreAsync(logicModule, snapshot, subject, actor, cancellationToken);
    }

    private async Task<InstalledLogicModule> RegisterAuthorizedCoreAsync(InstalledLogicModule logicModule, byte[]? archive,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logicModule);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        RequireBoundActor(subject, actor);
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
                if (archive is not null)
                {
                    await StorePackageAsync(current, archive, subject, actor, time, cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                // Registration never changes an installed version's approval
                // or retirement state, even if its caller proposes a different
                // initial trust classification for the same immutable content.
                await transaction.CommitAsync(cancellationToken);
                return current;
            }
            if (archive is not null)
                await StorePackageAsync(logicModule, archive, subject, actor, time, cancellationToken);
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
    private async Task StorePackageAsync(InstalledLogicModule module, byte[] archive, AccessSubject subject,
        SecurityActorContext actor, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await dbContext.LogicModulePackages.AnyAsync(item => item.Sha256 == module.PackageSha256, cancellationToken)) return;
        // Metadata-only approval must not become executable by attaching bytes later.
        if (module.TrustStatus != LogicModuleTrustStatus.Untrusted || module.IsEnabled)
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_REQUIRES_NEW_VERSION", FailureKind.Conflict,
                "Upload a new unapproved module version before supplying package content.");
        dbContext.LogicModulePackages.Add(new LogicModulePackageEntity
        {
            Sha256 = module.PackageSha256, Content = archive,
            UploadedAtUtc = time.GetUtcNow(), UploadedByUserId = subject.UserId
        });
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = time.GetUtcNow(), ActorUserId = subject.UserId,
            ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId,
            Action = "logic-module.package-uploaded", TargetType = "logic-module", TargetId = module.Id.ToString("D"),
            DetailsJson = JsonSerializer.Serialize(new { module.ModuleId, module.Version, module.PackageSha256, SizeBytes = archive.Length })
        });
    }

}
