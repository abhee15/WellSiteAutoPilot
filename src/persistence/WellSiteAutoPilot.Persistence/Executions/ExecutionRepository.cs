using System.Text.Json;
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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task AddRequestedAsync(
        ExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        dbContext.Executions.Add(ToEntity(execution));

        var messageId = Guid.NewGuid();
        var payload = new ExecutionRequestedV1(
            execution.Id,
            execution.LogicInstanceId,
            execution.ModuleId,
            execution.ModuleVersion,
            execution.ConfigurationRevisionId,
            execution.AssetId,
            execution.AssetExternalId,
            execution.Quantity,
            execution.Mode.ToString(),
            execution.RequestedAtUtc);

        var envelope = new MessageEnvelope<ExecutionRequestedV1>(
            messageId,
            Subjects.ExecutionRequestedV1,
            1,
            execution.RequestedAtUtc,
            execution.CorrelationId,
            null,
            payload);

        dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = messageId,
            Type = Subjects.ExecutionRequestedV1,
            Payload = JsonSerializer.Serialize(envelope, SerializerOptions),
            CreatedAtUtc = execution.RequestedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExecutionRecord?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Executions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == executionId,
                cancellationToken);

        return entity is null ? null : ToDomain(entity);
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
        StartedAtUtc = execution.StartedAtUtc,
        CompletedAtUtc = execution.CompletedAtUtc,
        ResultCode = execution.ResultCode,
        FailureCode = execution.FailureCode
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
        entity.StartedAtUtc,
        entity.CompletedAtUtc,
        entity.ResultCode,
        entity.FailureCode);
}
