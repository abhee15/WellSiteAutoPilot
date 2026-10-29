using Microsoft.EntityFrameworkCore;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.Core;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Executions;

await AuthorizedReadTransportChecks.RunAsync();
if (args.Contains("--read-transport-only", StringComparer.Ordinal)) return 0;

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
await using var connection = new NatsConnection(NatsConnectionOptions.Create(natsUrl,
    Environment.GetEnvironmentVariable("WSA_NATS_TOKEN")));

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

// Deterministically reproduce the stale outbox ordering of the shared
// TeamCity database. The earlier message is valid but is NOT our request.
var shadowCommand = new ShadowExecutionCommand(
    Guid.NewGuid(),
    "sample.shadow.logic",
    "1.0.0",
    Guid.NewGuid(),
    Guid.NewGuid(),
    "WELL-101",
    "PumpFillage");
_ = await service.RequestShadowAsync(
    shadowCommand with
    {
        LogicInstanceId = Guid.NewGuid(),
        ConfigurationRevisionId = Guid.NewGuid()
    },
    "ci-unrelated-v1-dispatch");
var execution = await service.RequestShadowAsync(
    shadowCommand, "ci-execution-dispatch");

var publisher = new OutboxPublisher(
    dbContext,
    connection,
    TimeProvider.System);

var dispatched = await publisher.DispatchBatchAsync();

if (dispatched < 1)
{
    throw new InvalidOperationException("No execution outbox message was dispatched.");
}

// Check the exact execution's row, not just any processed V1 outbox row.
var ownV1OutboxProcessed = await dbContext.OutboxMessages
    .AsNoTracking()
    .AnyAsync(message =>
        message.Type == Subjects.ExecutionRequestedV1 &&
        message.Payload.Contains(execution.Id.ToString("D")) &&
        message.ProcessedAtUtc != null);
if (!ownV1OutboxProcessed)
{
    throw new InvalidOperationException(
        "Dispatch did not process this test's V1 outbox record; " +
        "older pending outbox records may have exhausted the batch.");
}

// MaxMsgs=1 is not a guarantee that the first delivered message is ours.
// ConsumeAsync keeps pulling; continue beyond unrelated V1 messages.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
string? receivedPayload = null;
var skippedV1 = 0;
try
{
    await foreach (var message in consumer.ConsumeAsync<string>(
        opts: new NatsJSConsumeOpts { MaxMsgs = 1 },
        cancellationToken: timeout.Token))
    {
        await message.AckAsync(cancellationToken: timeout.Token);
        if (message.Data?.Contains(
                execution.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) == true)
        {
            receivedPayload = message.Data;
            break;
        }

        skippedV1++;
    }
}
catch (OperationCanceledException) when (timeout.IsCancellationRequested)
{
    // Turn elapsed cancellation into an informative test assertion below.
}

if (string.IsNullOrWhiteSpace(receivedPayload))
{
    throw new InvalidOperationException(
        $"The durable V1 execution {execution.Id:D} was not observed on JetStream; " +
        $"{skippedV1} unrelated messages arrived before the consumer stopped.");
}
if (skippedV1 < 1)
{
    throw new InvalidOperationException(
        "The V1 stale-outbox regression fixture did not produce an earlier unrelated message.");
}
Console.WriteLine($"V1 matched after {skippedV1} unrelated messages.");

var v2ConsumerName = $"execution-v2-dispatch-{Guid.NewGuid():N}";
var v2Consumer = await jetStream.CreateOrUpdateConsumerAsync(
    JetStreamTopology.ExecutionStream,
    new ConsumerConfig(v2ConsumerName)
    {
        AckPolicy = ConsumerConfigAckPolicy.Explicit,
        FilterSubject = Subjects.ExecutionRequestedV2
    });

var configuredAssetId = Guid.NewGuid();
var configuredCommand = new ConfiguredShadowExecutionCommand(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "weatherford.multi-input-test",
        "1.0.0",
        """{"target":75}""",
        [
            new ConfiguredExecutionAssetCommand(
                "well",
                configuredAssetId,
                "{}")
        ],
        [
            new ConfiguredExecutionInputCommand(
                "pump-fillage",
                configuredAssetId,
                "simulator",
                "WELL-101",
                "PumpFillage",
                "Current",
                "%",
                60,
                false,
                "{}")
        ],
        ExecutionTriggerKind.Scheduled,
        DateTimeOffset.UtcNow.AddMinutes(1));

// A separate earlier V2 outbox row exercises the same shared-database
// failure mode even when the native TeamCity stream starts empty.
_ = await service.RequestConfiguredShadowAsync(
    configuredCommand with
    {
        ConfiguredLogicId = Guid.NewGuid(),
        ConfigurationRevisionId = Guid.NewGuid(),
        ScheduledForUtc = DateTimeOffset.UtcNow.AddMinutes(2)
    },
    "ci-unrelated-v2-dispatch");
var configuredExecution = await service.RequestConfiguredShadowAsync(
    configuredCommand, "ci-execution-v2-dispatch");

var v2Dispatched = await publisher.DispatchBatchAsync();
if (v2Dispatched < 1)
{
    throw new InvalidOperationException(
        "No execution V2 outbox message was dispatched.");
}

// A successful batch count does not prove that the batch reached OUR row.
var ownV2OutboxProcessed = await dbContext.OutboxMessages
    .AsNoTracking()
    .AnyAsync(message =>
        message.Type == Subjects.ExecutionRequestedV2 &&
        message.Payload.Contains(configuredExecution.Id.ToString("D")) &&
        message.ProcessedAtUtc != null);
if (!ownV2OutboxProcessed)
{
    throw new InvalidOperationException(
        "Dispatch did not process this test's V2 outbox record; " +
        "older pending outbox records may have exhausted the batch.");
}

using var v2Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
string? v2ReceivedPayload = null;
var skippedV2 = 0;
try
{
    await foreach (var message in v2Consumer.ConsumeAsync<string>(
        opts: new NatsJSConsumeOpts { MaxMsgs = 1 },
        cancellationToken: v2Timeout.Token))
    {
        await message.AckAsync(cancellationToken: v2Timeout.Token);
        if (message.Data?.Contains(
                configuredExecution.Id.ToString("D"),
                StringComparison.OrdinalIgnoreCase) == true)
        {
            v2ReceivedPayload = message.Data;
            break;
        }

        skippedV2++;
    }
}
catch (OperationCanceledException) when (v2Timeout.IsCancellationRequested)
{
    // Keep the diagnostic count of unrelated messages below.
}

if (string.IsNullOrWhiteSpace(v2ReceivedPayload))
{
    throw new InvalidOperationException(
        $"The durable V2 execution {configuredExecution.Id:D} was not observed on JetStream; " +
        $"{skippedV2} unrelated messages arrived before the consumer stopped.");
}
if (skippedV2 < 1)
{
    throw new InvalidOperationException(
        "The V2 stale-outbox regression fixture did not produce an earlier unrelated message.");
}
Console.WriteLine($"V2 matched after {skippedV2} unrelated messages.");

await ModulePackagePublicationChecks.RunAsync(dbOptions, connection);
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection);
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, structured: true);
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, structured: true, structuredQuality: "Unknown");
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true);
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, disabledConnection: true);

await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, foreSitePayload: "scan");
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, foreSitePayload: "missing");
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, foreSitePayload: "current");
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, foreSitePayload: "future");
await WellSiteAutoPilot.ExecutionDispatchTests.HostedModuleExecutionChecks.RunAsync(dbOptions, connection, savedConnection: true, foreSitePayload: "invalid-time");

Console.WriteLine("Execution V1/V2 outbox to JetStream dispatch checks passed.");
return 0;
