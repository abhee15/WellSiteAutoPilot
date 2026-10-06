using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed class OutboxPublisher(
    WellSiteAutoPilotDbContext dbContext,
    INatsConnection connection,
    TimeProvider timeProvider)
{
    private const int BatchSize = 100;

    public async Task<int> DispatchBatchAsync(
        CancellationToken cancellationToken = default)
    {
        await JetStreamTopology.EnsureAsync(connection, cancellationToken);

        var pending = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAtUtc == null)
            .OrderBy(message => message.CreatedAtUtc)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);

        if (pending.Length == 0)
        {
            return 0;
        }

        var jetStream = connection.CreateJetStreamContext();

        foreach (var message in pending)
        {
            var acknowledgement = await jetStream.PublishAsync(
                message.Type,
                message.Payload,
                opts: new NatsJSPubOpts
                {
                    MsgId = message.Id.ToString("N")
                },
                cancellationToken: cancellationToken);

            acknowledgement.EnsureSuccess();

            message.ProcessedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return pending.Length;
    }
}
