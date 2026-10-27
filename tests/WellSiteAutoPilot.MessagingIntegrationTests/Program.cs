using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Worker.DotNet.Execution;

var natsUrl = Environment.GetEnvironmentVariable("WSA_NATS_URL") ?? "nats://127.0.0.1:4222";
var natsToken = Environment.GetEnvironmentVariable("WSA_NATS_TOKEN");
await using var connection = new NatsConnection(NatsConnectionOptions.Create(natsUrl, natsToken));
if (args.Contains("--verify-unauthenticated-package-refusal", StringComparer.Ordinal))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var destination = new MemoryStream();
    try { await new NatsModulePackageSource(connection).CopyToAsync(new string('a', 64), destination, deadline.Token); }
    catch (WellSiteAutoPilotException error) when (error.Code == "MODULE_PACKAGE_BROKER_AUTH_REQUIRED")
    {
        var anonymousReadiness = await new BrokerReadinessCheck(connection).CheckHealthAsync(new HealthCheckContext(), deadline.Token);
        if (anonymousReadiness.Status == HealthStatus.Healthy) throw new InvalidOperationException("Anonymous broker reported ready.");
        Console.WriteLine("Worker refused package retrieval from an unauthenticated broker.");
        return 0;
    }
    throw new InvalidOperationException("Worker accepted an unauthenticated package broker.");
}
if (!string.IsNullOrEmpty(natsToken)) await BrokerAuthenticationChecks.RunAsync(natsUrl);

await JetStreamTopology.EnsureAsync(connection);
var readiness = await new BrokerReadinessCheck(connection).CheckHealthAsync(new HealthCheckContext());
if (readiness.Status != HealthStatus.Healthy) throw new InvalidOperationException("Authenticated broker did not report ready.");

var jetStream = connection.CreateJetStreamContext();

await jetStream.GetStreamAsync(JetStreamTopology.ExecutionStream);
await jetStream.GetStreamAsync(JetStreamTopology.ControlStream);
await jetStream.GetStreamAsync(JetStreamTopology.IntegrationStream);

var messageId = Guid.NewGuid().ToString("N");
var first = await jetStream.PublishAsync(
    Subjects.ExecutionRequestedV1,
    "messaging-integration-probe",
    opts: new NatsJSPubOpts
    {
        MsgId = messageId
    });

first.EnsureSuccess();

var duplicate = await jetStream.PublishAsync(
    Subjects.ExecutionRequestedV1,
    "messaging-integration-probe",
    opts: new NatsJSPubOpts
    {
        MsgId = messageId
    });

if (!duplicate.Duplicate)
{
    throw new InvalidOperationException(
        "JetStream did not identify a repeated message ID as a duplicate.");
}

await ObjectStoreChecks.RunAsync(connection, args.Contains("--verify-package-after-restart", StringComparer.Ordinal));
Console.WriteLine("NATS JetStream topology and message deduplication checks passed.");
return 0;
