using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository
{
    public async Task<ManualShadowRequestReceipt> RequestManualShadowAuthorizedAsync(Guid configurationId, Guid revisionId,
        Guid requestId, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (configurationId == Guid.Empty || revisionId == Guid.Empty || requestId == Guid.Empty ||
            subject.UserId == Guid.Empty || actor.UserId != subject.UserId ||
            !ValidRequestText(actor.IdentityName, 256) || (actor.CorrelationId is not null && !ValidRequestText(actor.CorrelationId, 128)))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "A bound authenticated actor and valid request metadata are required.");
        var executionId = ManualShadowRequestService.DeriveExecutionId(subject.UserId, requestId);
        var time = clock ?? TimeProvider.System;
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await LockRequestKeyAsync(ManualRequestLock, executionId, cancellationToken);
            await LockRequestKeyAsync(ConfigurationDispatchLock, configurationId, cancellationToken);
            var configurations = new ConfiguredLogicRepository(dbContext, clock);
            var definition = await configurations.LockForManualRequestAsync(configurationId, subject, cancellationToken);
            var previous = await dbContext.Executions.FromSqlInterpolated($"""
                SELECT e.* FROM operations.executions AS e WHERE e."Id" = {executionId} FOR UPDATE OF e
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (previous.Length == 1)
            {
                var receipt = ManualShadowRequestService.Match(ToDomain(previous[0]), configurationId, revisionId);
                var scopes = await dbContext.ExecutionAssetScopes.FromSqlInterpolated($"""
                    SELECT s.* FROM operations.execution_asset_scopes AS s WHERE s."ExecutionId" = {executionId}
                    ORDER BY s."AssetId" FOR SHARE OF s
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
                var targets = scopes.Select(item => item.AssetId).Distinct().Order().ToArray();
                await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest, targets, time, cancellationToken);
                if (scopes.Any(item => item.LogicInstanceId != configurationId)) throw RequestConflict();
                await ValidateReceiptSnapshotAsync(receipt, targets, cancellationToken);
                // Recheck configured read proof after a potentially blocking
                // execution grant fence. Never return persisted result fields.
                await configurations.LockForManualRequestAsync(configurationId, subject, cancellationToken);
                await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest, targets, time, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Receipt(receipt);
            }

            var command = await configurations.PrepareLockedManualRequestAsync(definition, revisionId, subject, cancellationToken);
            var execution = ExecutionService.CreateConfiguredRequest(command, actor.CorrelationId ?? executionId.ToString("D"), time.GetUtcNow(), executionId);
            var assetIds = command.Assets.Select(item => item.AssetId).Distinct().ToArray();
            if (await HasActiveAssetOverlapAsync(configurationId, assetIds, cancellationToken))
                throw new WellSiteAutoPilotException("EXECUTION_ASSET_OVERLAP", FailureKind.Conflict,
                    "A configured execution is already pending or running.");
            execution = await StageConfiguredRequestAsync(execution, command, cancellationToken, subject);
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = execution.RequestedAtUtc, ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName.Trim(), CorrelationId = execution.CorrelationId,
                Action = "EXECUTION_SHADOW_REQUESTED", TargetType = "Execution", TargetId = executionId.ToString("D"),
                DetailsJson = JsonSerializer.Serialize(new { ConfiguredLogicId = configurationId, ConfigurationRevisionId = revisionId })
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Receipt(execution);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException("EXECUTION_REQUEST_CONFLICT", FailureKind.Conflict,
                "The request or its authority changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private static ManualShadowRequestReceipt Receipt(ExecutionRecord execution) => new(execution.Id, execution.LogicInstanceId,
        execution.ConfigurationRevisionId, execution.RequestContractVersion, execution.Mode, execution.Trigger);

    private static async Task ValidateReceiptSnapshotAsync(ExecutionRecord execution, Guid[] targets, CancellationToken cancellationToken)
    {
        try
        {
            if (execution.RequestPayloadJson is null || execution.AssetId is not null || execution.ScheduledForUtc is not null) throw RequestConflict();
            ExecutionRequestedV2 snapshot;
            if (execution.RequestContractVersion == 3)
            {
                var hosted = JsonSerializer.Deserialize<ExecutionRequestedV3>(execution.RequestPayloadJson, SerializerOptions) ?? throw RequestConflict();
                if (hosted.Execution is null || hosted.Package is null || hosted.Package.Sha256 is not { Length: 64 } hash ||
                    !hash.All(char.IsAsciiHexDigit) || hosted.Package.ReviewedManifestJson is null) throw RequestConflict();
                using var manifestStream = new MemoryStream(Encoding.UTF8.GetBytes(hosted.Package.ReviewedManifestJson));
                var manifest = await LogicModuleManifestReader.ReadAsync(manifestStream, cancellationToken);
                if (manifest.ModuleId != execution.ModuleId || manifest.Version != execution.ModuleVersion) throw RequestConflict();
                snapshot = hosted.Execution;
            }
            else snapshot = JsonSerializer.Deserialize<ExecutionRequestedV2>(execution.RequestPayloadJson, SerializerOptions) ?? throw RequestConflict();
            if (snapshot.Assets is null || snapshot.Inputs is null || snapshot.Assets.Any(item => item is null) ||
                snapshot.Inputs.Any(item => item is null) || string.IsNullOrWhiteSpace(snapshot.ParametersJson)) throw RequestConflict();
            var command = new ConfiguredShadowExecutionCommand(snapshot.ConfiguredLogicId, snapshot.ConfigurationRevisionId,
                snapshot.ModuleId, snapshot.ModuleVersion, snapshot.ParametersJson,
                snapshot.Assets.Select(item => new ConfiguredExecutionAssetCommand(item.Role, item.AssetId, item.ParameterOverridesJson)).ToArray(),
                snapshot.Inputs.Select(item => new ConfiguredExecutionInputCommand(item.RequirementId, item.AssetId, item.ProviderId,
                    item.ProviderAssetExternalId, item.Quantity, item.Access, item.CanonicalUnit, item.MaximumAgeSeconds,
                    item.AllowUncertainQuality, item.ProviderMappingJson)).ToArray(), ExecutionTriggerKind.Manual, null);
            _ = ExecutionService.CreateConfiguredRequest(command, execution.CorrelationId, execution.RequestedAtUtc, execution.Id);
            if (snapshot.ExecutionId != execution.Id || snapshot.ConfiguredLogicId != execution.LogicInstanceId || snapshot.ConfigurationRevisionId != execution.ConfigurationRevisionId ||
                snapshot.ModuleId != execution.ModuleId || snapshot.ModuleVersion != execution.ModuleVersion ||
                snapshot.Mode != nameof(ExecutionMode.Shadow) ||
                snapshot.RequestedAtUtc.UtcTicks / TimeSpan.TicksPerMicrosecond != execution.RequestedAtUtc.UtcTicks / TimeSpan.TicksPerMicrosecond ||
                !snapshot.Assets.Select(item => item.AssetId).Concat(snapshot.Inputs.Select(item => item.AssetId))
                    .Distinct().Order().SequenceEqual(targets)) throw RequestConflict();
        }
        catch (JsonException exception) { throw RequestConflict(exception); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation) { throw RequestConflict(exception); }
    }

    private static bool ValidRequestText(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static WellSiteAutoPilotException RequestConflict(Exception? inner = null) => new("EXECUTION_REQUEST_KEY_CONFLICT", FailureKind.Conflict,
        "The persisted request receipt does not match its execution scope.", innerException: inner);
}
