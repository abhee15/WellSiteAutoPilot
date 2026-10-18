using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;
using System.Security.Cryptography;
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

if (!duplicate.Duplicate)
{
    throw new InvalidOperationException(
        "JetStream did not identify a repeated message ID as a duplicate.");
}

// ADR-007: module packages travel through the JetStream Object Store keyed by SHA-256.
// Prove the pinned client and server round-trip a multi-chunk object byte for byte.
var objectStores = connection.CreateObjectStoreContext();
var bucket = $"wsa-probe-{Guid.NewGuid():N}";
var store = await objectStores.CreateObjectStoreAsync(bucket);

var payload = new byte[1024 * 1024 + 123];
RandomNumberGenerator.Fill(payload);
var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
var objectName = $"sha256/{digest}";

using (var upload = new MemoryStream(payload))
{
    await store.PutAsync(objectName, upload);
}

var metadata = await store.GetInfoAsync(objectName);
if (metadata.Size != (ulong)payload.Length || metadata.Chunks < 2)
{
    throw new InvalidOperationException(
        $"Object Store reported size {metadata.Size} in {metadata.Chunks} chunk(s); expected a multi-chunk object of {payload.Length} bytes.");
}

using (var download = new MemoryStream())
{
    await store.GetAsync(objectName, download);
    var downloadedDigest = Convert.ToHexString(SHA256.HashData(download.ToArray())).ToLowerInvariant();
    if (downloadedDigest != digest)
    {
        throw new InvalidOperationException(
            "Object Store returned bytes whose SHA-256 differs from the stored object.");
    }
}

// Publishing the same package again (an approval retried) must be harmless.
using (var repeat = new MemoryStream(payload))
{
    await store.PutAsync(objectName, repeat);
}

var missingRejected = false;
try
{
    using var sink = new MemoryStream();
    await store.GetAsync($"sha256/{new string('0', 64)}", sink);
}
catch (Exception)
{
    missingRejected = true;
}

if (!missingRejected)
{
    throw new InvalidOperationException("Object Store served an object that was never stored.");
}

Console.WriteLine("NATS JetStream topology, message deduplication and Object Store checks passed.");
return 0;
