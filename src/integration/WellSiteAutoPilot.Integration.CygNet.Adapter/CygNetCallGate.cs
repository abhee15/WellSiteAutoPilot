namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Every call slot for the service is held by calls that have not returned.</summary>
public sealed class CygNetBusyException() : Exception("CygNet is not answering and every call slot is in use.");

/// <summary>
/// The CygNet API is synchronous and cannot be cancelled. This runs each call on a pool thread, stops waiting for it
/// after a budget, and keeps its slot taken until the call really returns. A hung CygNet service can therefore hold at
/// most <c>slots</c> threads, and later calls fail fast instead of piling more blocked threads up.
/// </summary>
public sealed class CygNetCallGate(int slots = CygNetCallGate.DefaultSlots)
{
    public const int DefaultSlots = 4;

    public static CygNetCallGate Shared { get; } = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _services =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Throws <see cref="CygNetBusyException"/> when no slot is free and <see cref="TimeoutException"/> when the budget passes.</summary>
    public async Task<T> RunAsync<T>(string service, Func<T> work, TimeSpan budget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(work);
        var gate = _services.GetOrAdd(service, _ => new SemaphoreSlim(slots, slots));
        // A short wait rides out a brief burst; a full gate that stays full means CygNet has stopped answering.
        if (!await gate.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(500, budget.TotalMilliseconds)), cancellationToken))
            throw new CygNetBusyException();
        Task<T> call;
        try
        {
            call = Task.Run(() =>
            {
                try { return work(); }
                finally { gate.Release(); }
            }, CancellationToken.None);
        }
        catch
        {
            gate.Release();
            throw;
        }
        return await call.WaitAsync(budget, cancellationToken);
    }
}
