namespace WellSiteAutoPilot.Messaging.Nats;

/// <summary>
/// Extends the JetStream delivery lease during long-running work without
/// acknowledging completion prematurely. A progress failure cancels the work
/// and must prevent a final ACK, so the message can be recovered/redelivered.
/// </summary>
public static class JetStreamProgressLease
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> processAsync,
        Func<CancellationToken, ValueTask> acknowledgeProgressAsync,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processAsync);
        ArgumentNullException.ThrowIfNull(acknowledgeProgressAsync);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();

        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = ExtendLeaseAsync(
            acknowledgeProgressAsync, interval, lease);

        try
        {
            await processAsync(lease.Token);
        }
        finally
        {
            // Ensures no progress ACK occurs after processing has completed.
            // If the progress loop failed it will throw here, preventing a
            // final message ACK and preserving the original failure details.
            await lease.CancelAsync();
            await progressTask;
        }
    }

    private static async Task ExtendLeaseAsync(
        Func<CancellationToken, ValueTask> acknowledgeProgressAsync,
        TimeSpan interval,
        CancellationTokenSource lease)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(lease.Token))
            {
                await acknowledgeProgressAsync(lease.Token);
            }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // Expected only after work finishes or the host stops.
        }
        catch
        {
            // An uncertain delivery lease cannot safely complete the request.
            await lease.CancelAsync();
            throw;
        }
    }
}
