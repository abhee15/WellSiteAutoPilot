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

var execution = await service.RequestShadowAsync(
    command,
    "ci-shadow-execution");

if (execution.Mode != ExecutionMode.Shadow ||
    execution.Status != ExecutionStatus.Requested)
{
    throw new InvalidOperationException("Shadow execution was not created in Requested state.");
}

var roundTrip = await service.GetRequiredAsync(execution.Id);
if (roundTrip != execution)
{
    throw new InvalidOperationException("Persisted execution did not round-trip correctly.");
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

Console.WriteLine("Shadow execution persistence and outbox checks passed.");
return 0;
