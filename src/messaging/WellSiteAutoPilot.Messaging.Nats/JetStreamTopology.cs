using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace WellSiteAutoPilot.Messaging.Nats;

public static class JetStreamTopology
{
    public const string ExecutionStream = "WSA_EXECUTION";
    public const string ControlStream = "WSA_CONTROL";
    public const string IntegrationStream = "WSA_INTEGRATION";

    public static async ValueTask EnsureAsync(
        INatsConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.ConnectAsync();
        await connection.PingAsync(cancellationToken);

        var jetStream = connection.CreateJetStreamContext();

        await jetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(ExecutionStream, ["wsa.execution.>"])
            {
                Description = "WellSite AutoPilot durable execution messages"
            },
            cancellationToken);

        await jetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(ControlStream, ["wsa.control.>"])
            {
                Description = "WellSite AutoPilot governed control messages"
            },
            cancellationToken);

        await jetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(IntegrationStream, ["wsa.integration.>"])
            {
                Description = "WellSite AutoPilot integration events"
            },
            cancellationToken);
    }
}
