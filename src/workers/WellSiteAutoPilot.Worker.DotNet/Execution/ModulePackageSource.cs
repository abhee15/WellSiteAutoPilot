using NATS.Client.Core;
using NATS.Client.ObjectStore;
using NATS.Net;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

/// <summary>Copies exactly one requested immutable archive; never enumerates packages.</summary>
public interface IModulePackageSource
{
    Task CopyToAsync(string sha256, Stream destination, CancellationToken cancellationToken);
}

public sealed class NatsModulePackageSource(INatsConnection connection) : IModulePackageSource
{
    public async Task CopyToAsync(string sha256, Stream destination, CancellationToken cancellationToken)
    {
        var store = await connection.CreateObjectStoreContext()
            .GetObjectStoreAsync("WSA_MODULES", cancellationToken);
        await store.GetAsync("sha256/" + sha256, destination, cancellationToken: cancellationToken);
    }
}
