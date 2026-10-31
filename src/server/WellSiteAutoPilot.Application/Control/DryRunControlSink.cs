using System.Collections.Concurrent;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>
/// Read-only lookup of the current value a command would overwrite. Implementations must never write.
/// Returns null when the value cannot be determined.
/// </summary>
public interface IControlCurrentValueReader
{
    Task<decimal?> ReadCurrentAsync(Guid assetId, string command, CancellationToken cancellationToken = default);
}

public sealed class UnavailableControlCurrentValueReader : IControlCurrentValueReader
{
    public Task<decimal?> ReadCurrentAsync(Guid assetId, string command, CancellationToken cancellationToken = default) =>
        Task.FromResult<decimal?>(null);
}

public sealed record DryRunRecord(
    ControlDispatchRequest Request,
    decimal? CurrentValue,
    string? ReadbackError,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// Validates and records what WOULD be written, with a read-only current-value lookup. It has no write
/// dependency at all: the only collaborator is <see cref="IControlCurrentValueReader"/>.
/// </summary>
public sealed class DryRunControlCommandSink(IControlCurrentValueReader reader, TimeProvider timeProvider)
    : IControlCommandSink
{
    private readonly ConcurrentDictionary<string, DryRunRecord> records = new();

    public string Kind => Domain.Control.ControlSinkKinds.DryRun;

    public IReadOnlyCollection<DryRunRecord> Records => records.Values.ToArray();

    public async Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        if (records.TryGetValue(request.IdempotencyKey, out var existing))
        {
            return Result(request, existing);
        }

        decimal? current = null;
        string? error = null;
        try
        {
            current = await reader.ReadCurrentAsync(request.AssetId, request.Command, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.GetType().Name;
        }

        var record = records.GetOrAdd(request.IdempotencyKey,
            new DryRunRecord(request, current, error, timeProvider.GetUtcNow()));
        return Result(request, record);
    }

    private static ControlDispatchResult Result(ControlDispatchRequest request, DryRunRecord record)
    {
        var current = record.CurrentValue is { } value
            ? $"current {value}"
            : record.ReadbackError is null ? "current unavailable" : $"current read failed ({record.ReadbackError})";
        return new ControlDispatchResult(true, $"dryrun:{request.ActionId:N}",
            $"DRY RUN: would set {request.Command} to {request.Value} {request.Unit}; {current}. No write performed.");
    }
}
