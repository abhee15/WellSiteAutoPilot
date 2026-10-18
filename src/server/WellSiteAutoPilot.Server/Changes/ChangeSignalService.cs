using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;

namespace WellSiteAutoPilot.Server.Changes;

public interface IChangeSender
{
    Task SendAsync(ChangeArea area, CancellationToken cancellationToken);
}

/// <summary>Sends the area name to the hub group of connections admitted to that area.</summary>
public sealed class HubChangeSender(IHubContext<ChangesHub> hub) : IChangeSender
{
    public Task SendAsync(ChangeArea area, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChangeSignalProtocol.GroupName(area)).SendAsync(
            ChangeSignalProtocol.ClientMethod, ChangeAreaNames.ToWireName(area), cancellationToken);
}

/// <summary>
/// The server's <see cref="IChangeNotifier"/>. Producers only set a bit and wake the sender,
/// so a notification can neither fail nor slow the request that raised it. Bursts inside the
/// coalescing window collapse to one message per area, which loses nothing because only the
/// area is sent. In-process only: scale-out would need a backplane (ADR-008).
/// </summary>
public sealed partial class ChangeSignalService(IChangeSender sender, IOptions<ChangeSignalOptions> options,
    TimeProvider timeProvider, ILogger<ChangeSignalService> logger) : BackgroundService, IChangeNotifier
{
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private int pending;

    public void Notify(ChangeArea area)
    {
        try
        {
            if (!Enum.IsDefined(area)) return;
            Interlocked.Or(ref pending, 1 << (int)area);
            wake.Writer.TryWrite(true);
        }
        catch (Exception exception)
        {
            LogNotifyFailed(logger, exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var window = options.Value.CoalesceWindow;
        while (await wake.Reader.WaitToReadAsync(stoppingToken))
        {
            wake.Reader.TryRead(out _);
            if (window > TimeSpan.Zero) await Task.Delay(window, timeProvider, stoppingToken);
            await FlushAsync(stoppingToken);
        }
    }

    /// <summary>Sends every area raised since the last flush. Public for tests.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var bits = Interlocked.Exchange(ref pending, 0);
        foreach (var area in ChangeAreaNames.All)
        {
            if ((bits & (1 << (int)area)) == 0) continue;
            try
            {
                await sender.SendAsync(area, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogSendFailed(logger, exception, ChangeAreaNames.ToWireName(area));
            }
        }
    }

    [LoggerMessage(EventId = 2400, Level = LogLevel.Warning,
        Message = "A change notification could not be queued; clients recover by polling.")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Warning,
        Message = "A change notification for area {Area} could not be sent; clients recover by polling.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string area);
}
