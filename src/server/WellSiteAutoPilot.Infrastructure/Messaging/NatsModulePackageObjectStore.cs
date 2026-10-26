using System.Security.Cryptography;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.ObjectStore;
using NATS.Net;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed class NatsModulePackageObjectStore(INatsConnection connection) : IModulePackageObjectStore
{
    public const string BucketName = "WSA_MODULES";

    public async Task PutAsync(string packageSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content.Span)), packageSha256, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_HASH_MISMATCH", FailureKind.Validation,
                "Package bytes do not match the approved checksum.");
        var context = connection.CreateObjectStoreContext();
        INatsObjStore store;
        try { store = await context.GetObjectStoreAsync(BucketName, cancellationToken); }
        catch (NatsJSApiException exception) when (exception.Error.Code == 404)
        {
            try
            {
                store = await context.CreateObjectStoreAsync(new NatsObjConfig(BucketName)
                {
                    Storage = NatsObjStorageType.File
                }, cancellationToken);
            }
            catch (NatsJSApiException)
            {
                // Another Server may have created it while this one waited.
                // An unavailable/incompatible store still fails and is retried.
                store = await context.GetObjectStoreAsync(BucketName, cancellationToken);
            }
        }
        var stream = await context.JetStreamContext.GetStreamAsync("OBJ_" + BucketName, cancellationToken: cancellationToken);
        if (stream.Info.Config.Storage != StreamConfigStorage.File || stream.Info.Config.MaxAge != TimeSpan.Zero ||
            stream.Info.Config.Discard != StreamConfigDiscard.New)
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_STORE_NOT_DURABLE", FailureKind.DependencyUnavailable,
                "Package delivery requires a file-backed Object Store without automatic expiry or eviction.");
        using var upload = new MemoryStream(content.ToArray());
        await store.PutAsync("sha256/" + packageSha256, upload, cancellationToken: cancellationToken);
    }
}
