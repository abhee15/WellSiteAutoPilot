using System.Security.Cryptography;
using NATS.Client.Core;
using NATS.Client.ObjectStore;
using NATS.Net;

internal static class ObjectStoreChecks
{
    public static async Task RunAsync(NatsConnection connection, bool afterRestart)
    {
        const string bucket = "WSA_PACKAGE_PROBE";
        var bytes = new byte[1024 * 1024 + 123];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)((index * 31 + 7) % 251);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var name = "sha256/" + hash;
        var context = connection.CreateObjectStoreContext();
        var store = afterRestart ? await context.GetObjectStoreAsync(bucket) : await context.CreateObjectStoreAsync(bucket);
        if (!afterRestart)
        {
            using var first = new MemoryStream(bytes);
            await store.PutAsync(name, first);
            using var repeated = new MemoryStream(bytes);
            await store.PutAsync(name, repeated);
        }
        var info = await store.GetInfoAsync(name);
        if (info.Size != (ulong)bytes.Length || info.Chunks < 2)
            throw new InvalidOperationException("Package Object Store size/chunk metadata is incorrect.");
        using var download = new MemoryStream();
        await store.GetAsync(name, download);
        if (!bytes.SequenceEqual(download.ToArray()) || Convert.ToHexStringLower(SHA256.HashData(download.ToArray())) != hash)
            throw new InvalidOperationException("Package Object Store did not retain immutable bytes.");
        Console.WriteLine(afterRestart ? "Package Object Store retained multi-chunk content after server restart." :
            "Package Object Store multi-chunk round trip and repeated publication passed.");
    }
}
