using System.Text.Json;
using WellSiteAutoPilot.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed class ExecutionRepository(
    WellSiteAutoPilotDbContext dbContext) : IExecutionRepository
{
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

    public async Task AddConfiguredRequestedAsync(
        ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(command);

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
            command.Inputs
                .Select(input => new ExecutionInputBindingV2(
                    input.RequirementId,
                    input.AssetId,
                    input.ProviderId,
                    input.ProviderAssetExternalId,
                    input.Quantity,
                    input.Access,
                    input.CanonicalUnit,
                    input.MaximumAgeSeconds,
                    input.AllowUncertainQuality,
                    input.ProviderMappingJson))
                .ToArray(),
            execution.RequestedAtUtc);

        AddRequested(
            execution with
            {
                RequestContractVersion = 2,
                RequestPayloadJson = JsonSerializer.Serialize(payload, SerializerOptions)
            },
            Subjects.ExecutionRequestedV2,
            payload);

        AddAssetScopes(
            execution,
            command.Assets
                .Select(asset => asset.AssetId)
                .Distinct()
                .ToArray());

        await dbContext.SaveChangesAsync(cancellationToken);
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
            null,
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

    private static ExecutionRecord ToDomain(ExecutionEntity entity) => new(
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
