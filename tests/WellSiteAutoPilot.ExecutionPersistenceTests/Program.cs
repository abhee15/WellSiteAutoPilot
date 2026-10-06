using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Executions;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);
var repository = new ExecutionRepository(dbContext);
var service = new ExecutionService(repository, TimeProvider.System);

var command = new ShadowExecutionCommand(
    Guid.NewGuid(),
    "sample.shadow.logic",
    "1.0.0",
    Guid.NewGuid(),
    Guid.NewGuid(),
    "WELL-101",
    "PumpFillage");

var execution = await service.RequestShadowAsync(command, "ci-shadow-execution");

if (execution.Mode != ExecutionMode.Shadow || execution.Status != ExecutionStatus.Requested)
{
    throw new InvalidOperationException("Shadow execution was not created in Requested state.");
}

var roundTrip = await service.GetRequiredAsync(execution.Id);
if (roundTrip.Id != execution.Id ||
    roundTrip.LogicInstanceId != execution.LogicInstanceId ||
    roundTrip.ModuleId != execution.ModuleId ||
    roundTrip.ModuleVersion != execution.ModuleVersion ||
    roundTrip.ConfigurationRevisionId != execution.ConfigurationRevisionId ||
    roundTrip.AssetId != execution.AssetId ||
    roundTrip.AssetExternalId != execution.AssetExternalId ||
    roundTrip.Quantity != execution.Quantity ||
    roundTrip.Mode != execution.Mode ||
    roundTrip.Status != execution.Status ||
    roundTrip.CorrelationId != execution.CorrelationId)
{
    throw new InvalidOperationException("Persisted execution identity or contract fields did not round-trip correctly.");
}

if (Math.Abs((roundTrip.RequestedAtUtc - execution.RequestedAtUtc).TotalMilliseconds) > 1)
{
    throw new InvalidOperationException("Persisted execution timestamp drifted unexpectedly.");
}

var outbox = await dbContext.OutboxMessages
    .AsNoTracking()
    .SingleOrDefaultAsync(item => item.Type == Subjects.ExecutionRequestedV1);

if (outbox is null)
{
    throw new InvalidOperationException("Execution request did not create an outbox message.");
}

var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionRequestedV1>>(
    outbox.Payload,
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

if (envelope is null ||
    envelope.Payload.ExecutionId != execution.Id ||
    envelope.CorrelationId != execution.CorrelationId ||
    envelope.Payload.AssetExternalId != "WELL-101" ||
    envelope.Payload.Quantity != "PumpFillage")
{
    throw new InvalidOperationException("Execution outbox payload was incomplete or incorrect.");
}

var resultMessageId = Guid.NewGuid();
var startedAt = DateTimeOffset.UtcNow;
var completedAt = startedAt.AddSeconds(1);

var applied = await repository.ApplyCompletedAsync(
    resultMessageId,
    "ci-result-consumer",
    execution.Id,
    startedAt,
    completedAt,
    "SHADOW_OBSERVATION_COMPLETED",
    "{\"quantity\":\"PumpFillage\",\"value\":82}");

if (!applied)
{
    throw new InvalidOperationException("Execution completion result was not applied.");
}

var completed = await service.GetRequiredAsync(execution.Id);
if (completed.Status != ExecutionStatus.Completed ||
    completed.ResultCode != "SHADOW_OBSERVATION_COMPLETED" ||
    string.IsNullOrWhiteSpace(completed.OutputJson))
{
    throw new InvalidOperationException("Execution completion state was not persisted.");
}

var duplicateApplied = await repository.ApplyCompletedAsync(
    resultMessageId,
    "ci-result-consumer",
    execution.Id,
    startedAt,
    completedAt,
    "SHADOW_OBSERVATION_COMPLETED",
    completed.OutputJson);

if (duplicateApplied)
{
    throw new InvalidOperationException("Duplicate result message was not rejected by the Inbox.");
}

var recentExecutions = await service.ListAsync(
    nameof(ExecutionStatus.Completed),
    10);

if (!recentExecutions.Any(item => item.Id == execution.Id))
{
    throw new InvalidOperationException(
        "Completed execution was not returned by the recent execution query.");
}

Console.WriteLine("Shadow execution persistence, outbox, result, Inbox, and activity query checks passed.");
return 0;
