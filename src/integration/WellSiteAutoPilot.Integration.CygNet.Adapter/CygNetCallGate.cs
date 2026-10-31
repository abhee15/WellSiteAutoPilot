namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>The native API cannot reliably cancel. A timed-out operation retains its slot until it really ends.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Process-lifetime native lane; callers may leave while native work retains the semaphore. No wait handle is created.")]
public sealed class CygNetCallGate
{
    private readonly SemaphoreSlim _slot = new(1, 1);
    public async Task<T> RunAsync<T>(Func<CancellationToken, T> operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);
        var token = budget.Token;
        await _slot.WaitAsync(token);
        var task = Task.Run(() =>
        {
            try { token.ThrowIfCancellationRequested(); return operation(token); }
            finally { _slot.Release(); }
        }, CancellationToken.None);
        try { return await task.WaitAsync(token); }
        catch
        {
            // Observe an eventual vendor fault even after the waiting request has left.
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
