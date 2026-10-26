using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;
using NATS.Net;
using WellSiteAutoPilot.Failures;

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
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            if (connection.ServerInfo?.AuthRequired != true)
                throw new WellSiteAutoPilotException("MODULE_PACKAGE_BROKER_AUTH_REQUIRED", FailureKind.DependencyUnavailable,
                    "Package hosting requires an authenticated broker.");
            var store = await connection.CreateObjectStoreContext()
                .GetObjectStoreAsync("WSA_MODULES", cancellationToken);
            await store.GetAsync("sha256/" + sha256, destination, leaveOpen: true, cancellationToken: cancellationToken);
        }
        catch (NatsObjNotFoundException error)
        {
            throw Missing(error);
        }
        catch (NatsJSApiException error) when (error.Error.Code == 404)
        {
            throw Missing(error);
        }
        catch (NatsObjException error)
        {
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_INVALID", FailureKind.Validation,
                "The module package transport integrity check failed.", innerException: error);
        }
    }

    private static WellSiteAutoPilotException Missing(Exception error) =>
        new("MODULE_PACKAGE_NOT_AVAILABLE", FailureKind.DependencyUnavailable,
            "The approved module package has not been delivered to this worker.", retryable: true, innerException: error);
}
