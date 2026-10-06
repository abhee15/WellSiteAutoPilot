using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Nats;

var natsUrl = Environment.GetEnvironmentVariable("WSA_NATS_URL") ?? "nats://127.0.0.1:4222";

await using var connection = new NatsConnection(new NatsOpts
{
    Url = natsUrl
});

await JetStreamTopology.EnsureAsync(connection);

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

duplicate.EnsureSuccess();

if (!duplicate.Duplicate)
{
    throw new InvalidOperationException(
        "JetStream did not identify a repeated message ID as a duplicate.");
}

Console.WriteLine("NATS JetStream topology and message deduplication checks passed.");
return 0;
