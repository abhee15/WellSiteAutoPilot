using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using WellSiteAutoPilot.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository(
    WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IExecutionRepository, IExecutionReadRepository
{
    private const int ManualRequestLock = 1;
    private const int ConfigurationDispatchLock = 2;
    private const int StartedMessageLock = 3;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] ActiveExecutionStatuses =
    [
        nameof(ExecutionStatus.Requested),
        nameof(ExecutionStatus.Queued),
        nameof(ExecutionStatus.Starting),
        nameof(ExecutionStatus.Running),
        nameof(ExecutionStatus.Waiting)
    ];

    private static readonly string[] TerminalExecutionStatuses =
    [
        nameof(ExecutionStatus.Completed),
        nameof(ExecutionStatus.Failed),
        nameof(ExecutionStatus.Cancelled),
        nameof(ExecutionStatus.Suspended),
        nameof(ExecutionStatus.TimedOut)
    ];

    public async Task AddRequestedAsync(
        ExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        if (execution.AssetId is null ||
            string.IsNullOrWhiteSpace(execution.AssetExternalId) ||
            string.IsNullOrWhiteSpace(execution.Quantity))
        {
            throw new InvalidOperationException(
                "Execution V1 requires the legacy single-input Asset projection.");
        }

        var payload = new ExecutionRequestedV1(
            execution.Id,
            execution.LogicInstanceId,
            execution.ModuleId,
            execution.ModuleVersion,
            execution.ConfigurationRevisionId,
            execution.AssetId.Value,
            execution.AssetExternalId,
            execution.Quantity,
            execution.Mode.ToString(),
            execution.RequestedAtUtc);

        AddRequested(
            execution with
            {
                RequestContractVersion = 1,
                RequestPayloadJson = JsonSerializer.Serialize(payload, SerializerOptions)
            },
            Subjects.ExecutionRequestedV1,
            payload);

        AddAssetScopes(
            execution,
            [execution.AssetId.Value]);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ExecutionRecord> AddConfiguredRequestedAsync(ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command, CancellationToken cancellationToken = default) =>
        AddConfiguredRequestedAsync(execution, command, null, cancellationToken);

    public async Task<ExecutionRecord> AddConfiguredRequestedAsync(
        ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command,
        AccessSubject? readSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(command);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockRequestKeyAsync(ConfigurationDispatchLock, command.ConfiguredLogicId, cancellationToken);
        if (command.Trigger == ExecutionTriggerKind.Scheduled && command.ScheduledForUtc is { } occurrence &&
            await ScheduledOccurrenceExistsAsync(command.ConfigurationRevisionId, occurrence, cancellationToken))
            throw new WellSiteAutoPilotException("EXECUTION_SCHEDULED_OCCURRENCE_EXISTS", FailureKind.Conflict,
                "This scheduled occurrence has already been dispatched.");
        if (command.Trigger == ExecutionTriggerKind.Scheduled &&
            await HasActiveAssetOverlapAsync(command.ConfiguredLogicId,
                command.Assets.Select(a => a.AssetId).ToArray(), cancellationToken))
            throw new WellSiteAutoPilotException("EXECUTION_ASSET_OVERLAP", FailureKind.Conflict,
                "A configured execution is already pending or running.");
        await RequireEnabledViewConnectionAsync(command.ConfigurationRevisionId, cancellationToken);
        execution = await StageConfiguredRequestAsync(execution, command, cancellationToken, readSubject);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return execution;
    }

    private async Task RequireEnabledViewConnectionAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        var connectionId = await dbContext.ConfiguredLogicRevisions.Where(revision => revision.Id == revisionId)
            .Select(revision => revision.ViewConnectionId).SingleOrDefaultAsync(cancellationToken);
        if (connectionId is not { } id) return;
        var connections = await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {id} FOR SHARE OF c
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (connections.Length != 1 || !connections[0].IsEnabled)
            throw new WellSiteAutoPilotException("EXECUTION_CONNECTION_DISABLED", FailureKind.Conflict,
                "The assignment's Connection is disabled.");
    }

    private async Task<ExecutionRecord> StageConfiguredRequestAsync(ExecutionRecord execution, ConfiguredShadowExecutionCommand command,
        CancellationToken cancellationToken, AccessSubject? readSubject = null)
    {
        var inputs = await PinReadSourcesAsync(command, readSubject, cancellationToken);
        var payload = new ExecutionRequestedV2(
            execution.Id,
            command.ConfiguredLogicId,
            command.ConfigurationRevisionId,
            command.ModuleId,
            command.ModuleVersion,
            execution.Mode.ToString(),
            command.ParametersJson,
            command.Assets
                .Select(asset => new ExecutionAssetBindingV2(
                    asset.Role,
                    asset.AssetId,
                    asset.ParameterOverridesJson))
                .ToArray(),
            inputs,
            execution.RequestedAtUtc);

        var package = await PinModulePackageAsync(command.ModuleId, command.ModuleVersion, cancellationToken);
        if (package is null)
        {
            if (inputs.Any(input => input.Source is not null)) throw ReadFailure("EXECUTION_READ_PACKAGE_REQUIRED");
            execution = execution with { RequestContractVersion = 2, RequestPayloadJson = JsonSerializer.Serialize(payload, SerializerOptions) };
            AddRequested(execution, Subjects.ExecutionRequestedV2, payload);
        }
        else
        {
            var hosted = new ExecutionRequestedV3(payload, package);
            execution = execution with { RequestContractVersion = 3, RequestPayloadJson = JsonSerializer.Serialize(hosted, SerializerOptions) };
            AddRequested(execution, Subjects.ExecutionRequestedV3, hosted);
        }

        if (inputs.Any(input => input.Source is not null))
            dbContext.Executions.Local.Single(item => item.Id == execution.Id).ReadAuthorityJson =
                JsonSerializer.Serialize(ReadAuthority.Create(readSubject!, execution.RequestedAtUtc), SerializerOptions);

        AddAssetScopes(
            execution,
            command.Assets
                .Select(asset => asset.AssetId)
                .Distinct()
                .ToArray());
        return execution;
    }

    public async Task<ExecutionRecord> AddManualConfiguredRequestedAsync(ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Request-key then configuration locks have a fixed order. Scheduled
        // dispatch uses the same configuration advisory lock for overlap checks.
        await LockRequestKeyAsync(ManualRequestLock, execution.Id, cancellationToken);
        var previous = await GetAsync(execution.Id, cancellationToken);
        if (previous is not null)
            return ManualShadowRequestService.Match(previous, command.ConfiguredLogicId, command.ConfigurationRevisionId);
        await LockRequestKeyAsync(ConfigurationDispatchLock, command.ConfiguredLogicId, cancellationToken);
        var definition = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated(
            $"SELECT * FROM logic.configured_logic WHERE \"Id\" = {command.ConfiguredLogicId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);
        var active = await dbContext.ConfiguredLogicRevisions.AsNoTracking().AnyAsync(r =>
            r.Id == command.ConfigurationRevisionId && r.ConfiguredLogicId == command.ConfiguredLogicId && r.Status == "Active", cancellationToken);
        var enabled = await dbContext.LogicModules.AsNoTracking().AnyAsync(m =>
            m.ModuleId == command.ModuleId && m.Version == command.ModuleVersion && m.IsEnabled, cancellationToken);
        var assetIds = command.Assets.Select(a => a.AssetId).Distinct().ToArray();
        var activeAssets = await dbContext.Assets.AsNoTracking().CountAsync(a => assetIds.Contains(a.Id) && a.IsActive, cancellationToken);
        if (definition.ActiveRevisionId != command.ConfigurationRevisionId || !active || !enabled || activeAssets != assetIds.Length)
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_REVISION_CONFLICT", FailureKind.Conflict,
                "The reviewed revision or its dependencies changed before dispatch.");
        if (await HasActiveAssetOverlapAsync(command.ConfiguredLogicId, assetIds, cancellationToken))
            throw new WellSiteAutoPilotException("EXECUTION_ASSET_OVERLAP", FailureKind.Conflict,
                "A configured execution is already pending or running.");
        await RequireEnabledViewConnectionAsync(command.ConfigurationRevisionId, cancellationToken);
        execution = await StageConfiguredRequestAsync(execution, command, cancellationToken,
            actor.UserId is { } userId ? new AccessSubject(userId) : null);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = execution.RequestedAtUtc,
            ActorUserId = actor.UserId, ActorIdentity = actor.IdentityName,
            Action = "EXECUTION_SHADOW_REQUESTED", TargetType = "Execution", TargetId = execution.Id.ToString("D"),
            CorrelationId = execution.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new { command.ConfiguredLogicId, command.ConfigurationRevisionId })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return execution;
    }

    private Task<int> LockRequestKeyAsync(int lockClass, Guid id, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockClass}, {BinaryPrimitives.ReadInt32BigEndian(id.ToByteArray())})", cancellationToken);

    public async Task ApplyStartedAsync(Guid messageId, string consumer, Guid executionId,
        DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Serialize exact-message replays without relying on a unique-violation catch.
        await LockRequestKeyAsync(StartedMessageLock, messageId, cancellationToken);
        if (await dbContext.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken)) return;
        await dbContext.Executions.Where(e => e.Id == executionId && (e.RequestContractVersion == 2 || e.RequestContractVersion == 3) &&
            (e.Status == "Requested" || e.Status == "Queued" || e.Status == "Starting"))
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, "Running")
                .SetProperty(e => e.StartedAtUtc, startedAtUtc), cancellationToken);
        dbContext.InboxMessages.Add(new InboxMessageEntity
        {
            MessageId = messageId, Consumer = consumer, ProcessedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ExecutionRecord?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Executions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == executionId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<ExecutionRecord?> GetInScopeAsync(
        Guid executionId,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            return null;
        }

        var ids = allowedAssetIds.Distinct().ToArray();
        var entity = await dbContext.Executions
            .AsNoTracking()
            .Where(item => item.Id == executionId)
            .Where(execution =>
                dbContext.ExecutionAssetScopes.Any(
                    scope => scope.ExecutionId == execution.Id) &&
                !dbContext.ExecutionAssetScopes.Any(
                    scope =>
                        scope.ExecutionId == execution.Id &&
                        !ids.Contains(scope.AssetId)))
            .SingleOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<ExecutionRecord>> ListAsync(
        ExecutionStatus? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Executions.AsNoTracking();

        if (status is not null)
        {
            var statusName = status.Value.ToString();
            query = query.Where(item => item.Status == statusName);
        }

        return (await query
            .OrderByDescending(item => item.RequestedAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<ExecutionRecord>> ListInScopeAsync(
        ExecutionStatus? status,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            return [];
        }

        var ids = allowedAssetIds.Distinct().ToArray();
        var query = dbContext.Executions
            .AsNoTracking()
            .Where(execution =>
                dbContext.ExecutionAssetScopes.Any(
                    scope => scope.ExecutionId == execution.Id) &&
                !dbContext.ExecutionAssetScopes.Any(
                    scope =>
                        scope.ExecutionId == execution.Id &&
                        !ids.Contains(scope.AssetId)));

        if (status is not null)
        {
            var statusName = status.Value.ToString();
            query = query.Where(item => item.Status == statusName);
        }

        return (await query
            .OrderByDescending(item => item.RequestedAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<Guid>> GetAssetScopeAsync(
        Guid executionId,
        CancellationToken cancellationToken = default) =>
        await dbContext.ExecutionAssetScopes
            .AsNoTracking()
            .Where(item => item.ExecutionId == executionId)
            .OrderBy(item => item.AssetId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

    public Task<bool> ScheduledOccurrenceExistsAsync(
        Guid configurationRevisionId,
        DateTimeOffset scheduledForUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.Executions
            .AsNoTracking()
            .AnyAsync(
                item => item.ConfigurationRevisionId == configurationRevisionId &&
                        item.ScheduledForUtc == scheduledForUtc,
                cancellationToken);

    public async Task<bool> HasActiveAssetOverlapAsync(
        Guid logicInstanceId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var ids = assetIds
            .Where(item => item != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return false;
        }

        return await (
            from scope in dbContext.ExecutionAssetScopes.AsNoTracking()
            join execution in dbContext.Executions.AsNoTracking()
                on scope.ExecutionId equals execution.Id
            where scope.LogicInstanceId == logicInstanceId &&
                  ids.Contains(scope.AssetId) &&
                  ActiveExecutionStatuses.Contains(execution.Status)
            select scope.ExecutionId)
            .AnyAsync(cancellationToken);
    }

    public Task<ExecutionResultDisposition> ApplyCompletedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string resultCode,
        string? outputJson,
        CancellationToken cancellationToken = default) =>
        ApplyResultAsync(
            messageId,
            consumer,
            executionId,
            ExecutionStatus.Completed,
            startedAtUtc,
            completedAtUtc,
            resultCode,
            null,
            outputJson,
            cancellationToken);

    public Task<ExecutionResultDisposition> ApplyFailedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken = default) =>
        ApplyResultAsync(
            messageId,
            consumer,
            executionId,
            ExecutionStatus.Failed,
            startedAtUtc,
            failedAtUtc,
            null,
            failureCode,
            null,
            cancellationToken);

    private void AddAssetScopes(
        ExecutionRecord execution,
        IReadOnlyCollection<Guid> assetIds)
    {
        foreach (var assetId in assetIds.Distinct())
        {
            dbContext.ExecutionAssetScopes.Add(new ExecutionAssetScopeEntity
            {
                ExecutionId = execution.Id,
                LogicInstanceId = execution.LogicInstanceId,
                AssetId = assetId
            });
        }
    }

    private void AddRequested<TPayload>(
        ExecutionRecord execution,
        string subject,
        TPayload payload)
    {
        dbContext.Executions.Add(ToEntity(execution));

        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope<TPayload>(
            messageId,
            subject,
            execution.RequestContractVersion,
            execution.RequestedAtUtc,
            execution.CorrelationId,
            Activity.Current?.Id,
            payload);

        dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = messageId,
            Type = subject,
            Payload = JsonSerializer.Serialize(envelope, SerializerOptions),
            CreatedAtUtc = execution.RequestedAtUtc
        });
    }

    private async Task<ExecutionResultDisposition> ApplyResultAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        ExecutionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? resultCode,
        string? failureCode,
        string? outputJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        // A conditional UPDATE serializes competing terminal outcomes at the
        // database row, not at the worker or EF change-tracking layer. The
        // Inbox and conflict audit must commit in the same transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        if (await dbContext.InboxMessages.AnyAsync(
                message => message.MessageId == messageId &&
                           message.Consumer == consumer,
                cancellationToken))
        {
            return ExecutionResultDisposition.AlreadyProcessed;
        }

        var incomingStatus = status.ToString();
        var changed = await dbContext.Executions
            .Where(item => item.Id == executionId &&
                           !TerminalExecutionStatuses.Contains(item.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, incomingStatus)
                .SetProperty(item => item.StartedAtUtc, startedAtUtc)
                .SetProperty(item => item.CompletedAtUtc, completedAtUtc)
                .SetProperty(item => item.ResultCode, resultCode)
                .SetProperty(item => item.FailureCode, failureCode)
                .SetProperty(item => item.OutputJson, outputJson),
                cancellationToken);

        ExecutionResultDisposition disposition;

        if (changed == 1)
        {
            disposition = ExecutionResultDisposition.Applied;
        }
        else
        {
            // This read occurs after the conditional update. PostgreSQL
            // serializes concurrent updates to the same execution row, so we
            // see the canonical winner of a competing terminal transition.
            var existing = await dbContext.Executions
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == executionId, cancellationToken) ??
                throw new KeyNotFoundException($"Execution {executionId} does not exist.");

            var matches = string.Equals(existing.Status, incomingStatus, StringComparison.Ordinal) &&
                          string.Equals(existing.ResultCode, resultCode, StringComparison.Ordinal) &&
                          string.Equals(existing.FailureCode, failureCode, StringComparison.Ordinal) &&
                          string.Equals(existing.OutputJson, outputJson, StringComparison.Ordinal);

            disposition = matches
                ? ExecutionResultDisposition.MatchingTerminalResult
                : ExecutionResultDisposition.ConflictingTerminalResult;

            if (!matches)
            {
                // Exclude module output and potentially sensitive values.
                // Persist the conflicting observation even though it may not
                // alter the canonical execution result.
                dbContext.AuditEvents.Add(new AuditEventEntity
                {
                    Id = Guid.NewGuid(),
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    ActorIdentity = "system:execution-result-consumer",
                    Action = "execution.result.conflict",
                    TargetType = "Execution",
                    TargetId = executionId.ToString("D"),
                    CorrelationId = existing.CorrelationId,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        messageId,
                        consumer,
                        existingStatus = existing.Status,
                        incomingStatus,
                        existingResultCode = existing.ResultCode,
                        incomingResultCode = resultCode,
                        existingFailureCode = existing.FailureCode,
                        incomingFailureCode = failureCode,
                        startedAtUtc,
                        terminalAtUtc = completedAtUtc
                    }, SerializerOptions)
                });
            }
        }

        dbContext.InboxMessages.Add(new InboxMessageEntity
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return disposition;
    }

    private static ExecutionEntity ToEntity(ExecutionRecord execution) => new()
    {
        Id = execution.Id,
        LogicInstanceId = execution.LogicInstanceId,
        ModuleId = execution.ModuleId,
        ModuleVersion = execution.ModuleVersion,
        ConfigurationRevisionId = execution.ConfigurationRevisionId,
        AssetId = execution.AssetId,
        AssetExternalId = execution.AssetExternalId,
        Quantity = execution.Quantity,
        Mode = execution.Mode.ToString(),
        Status = execution.Status.ToString(),
        CorrelationId = execution.CorrelationId,
        RequestedAtUtc = execution.RequestedAtUtc,
        RequestContractVersion = execution.RequestContractVersion,
        Trigger = execution.Trigger.ToString(),
        ScheduledForUtc = execution.ScheduledForUtc,
        RequestPayloadJson = execution.RequestPayloadJson,
        StartedAtUtc = execution.StartedAtUtc,
        CompletedAtUtc = execution.CompletedAtUtc,
        ResultCode = execution.ResultCode,
        FailureCode = execution.FailureCode,
        OutputJson = execution.OutputJson
    };

    internal static ExecutionRecord ToDomain(ExecutionEntity entity) => new(
        entity.Id,
        entity.LogicInstanceId,
        entity.ModuleId,
        entity.ModuleVersion,
        entity.ConfigurationRevisionId,
        entity.AssetId,
        entity.AssetExternalId,
        entity.Quantity,
        Enum.Parse<ExecutionMode>(entity.Mode),
        Enum.Parse<ExecutionStatus>(entity.Status),
        entity.CorrelationId,
        entity.RequestedAtUtc,
        entity.RequestContractVersion,
        Enum.Parse<ExecutionTriggerKind>(entity.Trigger),
        entity.ScheduledForUtc,
        entity.RequestPayloadJson,
        entity.StartedAtUtc,
        entity.CompletedAtUtc,
        entity.ResultCode,
        entity.FailureCode,
        entity.OutputJson);
}
