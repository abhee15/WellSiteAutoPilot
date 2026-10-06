using Microsoft.EntityFrameworkCore;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.Core;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Executions;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var natsUrl = Environment.GetEnvironmentVariable("WSA_NATS_URL") ?? "nats://127.0.0.1:4222";

var dbOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(dbOptions);
await using var connection = new NatsConnection(new NatsOpts
{
    Url = natsUrl
});

await JetStreamTopology.EnsureAsync(connection);

var jetStream = connection.CreateJetStreamContext();
var consumerName = $"execution-dispatch-{Guid.NewGuid():N}";
var consumer = await jetStream.CreateOrUpdateConsumerAsync(
    JetStreamTopology.ExecutionStream,
    new ConsumerConfig(consumerName)
    {
        AckPolicy = ConsumerConfigAckPolicy.Explicit,
        FilterSubject = Subjects.ExecutionRequestedV1
    });

var repository = new ExecutionRepository(dbContext);
var service = new ExecutionService(repository, TimeProvider.System);

var execution = await service.RequestShadowAsync(
    new ShadowExecutionCommand(
        Guid.NewGuid(),
        "sample.shadow.logic",
        "1.0.0",
        Guid.NewGuid(),
        Guid.NewGuid(),
        "WELL-101",
        "PumpFillage"),
    "ci-execution-dispatch");

var publisher = new OutboxPublisher(
    dbContext,
    connection,
    TimeProvider.System);

var dispatched = await publisher.DispatchBatchAsync();

if (dispatched < 1)
{
    throw new InvalidOperationException("No execution outbox message was dispatched.");
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
string? receivedPayload = null;

await foreach (var message in consumer.ConsumeAsync<string>(
    opts: new NatsJSConsumeOpts
    {
        MaxMsgs = 1
    },
    cancellationToken: timeout.Token))
{
    receivedPayload = message.Data;
    await message.AckAsync(cancellationToken: timeout.Token);
    break;
}

if (string.IsNullOrWhiteSpace(receivedPayload) ||
    !receivedPayload.Contains(execution.Id.ToString(), StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "The durable execution request was not observed on JetStream.");
}

var outboxProcessed = await dbContext.OutboxMessages
    .AsNoTracking()
    .AnyAsync(
        message =>
            message.Type == Subjects.ExecutionRequestedV1 &&
            message.ProcessedAtUtc != null);

if (!outboxProcessed)
{
    throw new InvalidOperationException(
        "The dispatched outbox message was not marked processed.");
}

Console.WriteLine("Execution outbox to JetStream dispatch checks passed.");
return 0;
