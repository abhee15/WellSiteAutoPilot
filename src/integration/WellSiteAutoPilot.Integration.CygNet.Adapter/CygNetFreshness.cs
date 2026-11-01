using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>What a few sample reads say about a connection's points. <see cref="Broken"/> lists mapped quantities whose point is on none of the sampled facilities.</summary>
public sealed record CygNetFreshnessReport(string Summary, TimeSpan? Oldest, IReadOnlyList<string> Broken);

/// <summary>
/// Samples current values on a few facilities so a connection test or discovery can say how old the data is and
/// whether the mapped points exist. Demo and idle CygNet services hold values that are years old; a module's
/// maximumAgeSeconds must allow for that, and this makes it visible before a run fails on it.
/// </summary>
public static class CygNetFreshness
{
    public const int FacilitiesSampled = 3;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    /// <summary>Evenly spaced picks so the sample is not just the first few ids.</summary>
    public static IReadOnlyList<string> Pick(IReadOnlyList<string> ids, int count = FacilitiesSampled)
    {
        if (ids.Count <= count) return ids;
        return Enumerable.Range(0, count).Select(i => ids[i * (ids.Count - 1) / (count - 1)]).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Returns null when nothing could be sampled (no facilities, or the budget passed before any read).</summary>
    public static async Task<CygNetFreshnessReport?> SampleAsync(
        ICygNetClient client, CygNetConnection connection, IReadOnlyList<string> facilityIds, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (facilityIds.Count == 0) return null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        var ages = new Dictionary<string, List<TimeSpan>>(StringComparer.Ordinal);
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        var empty = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmapped = new HashSet<string>(StringComparer.Ordinal);
        var sampled = 0;
        foreach (var quantity in AdapterReadRules.CygNetQuantities.Order(StringComparer.Ordinal))
        {
            foreach (var id in facilityIds)
            {
                try
                {
                    var value = await client.ReadCurrentAsync(connection, id, quantity, budget.Token);
                    sampled++;
                    (ages.TryGetValue(quantity, out var list) ? list : ages[quantity] = []).Add(clock.GetUtcNow() - value.TimestampUtc);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Build(ages, missing, empty, unmapped, facilityIds.Count, sampled, partial: true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    sampled++;
                    var code = error switch
                    {
                        CygNetReadException read => read.Code,
                        WellSiteAutoPilotException failure => failure.Code,
                        _ => "CYGNET_READ_FAILED"
                    };
                    var bucket = code switch
                    {
                        "CYGNET_QUANTITY_UNSUPPORTED" => null,
                        "CYGNET_POINT_NO_VALUE" => empty,
                        _ => missing
                    };
                    if (bucket is null) unmapped.Add(quantity);
                    else bucket[quantity] = bucket.GetValueOrDefault(quantity) + 1;
                }
            }
        }
        return Build(ages, missing, empty, unmapped, facilityIds.Count, sampled, partial: false);
    }

    private static CygNetFreshnessReport? Build(
        Dictionary<string, List<TimeSpan>> ages, Dictionary<string, int> missing, Dictionary<string, int> empty,
        HashSet<string> unmapped, int facilities, int sampled, bool partial)
    {
        if (sampled == 0) return null;
        var parts = new List<string>();
        var broken = new List<string>();
        TimeSpan? oldest = null;
        foreach (var quantity in AdapterReadRules.CygNetQuantities.Order(StringComparer.Ordinal))
        {
            if (unmapped.Contains(quantity)) { parts.Add($"{quantity} not mapped"); continue; }
            var notes = new List<string>();
            if (ages.TryGetValue(quantity, out var list) && list.Count > 0)
            {
                var low = list.Min();
                var high = list.Max();
                notes.Add(low == high || CygNetValues.FormatAge(low) == CygNetValues.FormatAge(high)
                    ? CygNetValues.FormatAge(high) + " old"
                    : $"{CygNetValues.FormatAge(low)} to {CygNetValues.FormatAge(high)} old");
                oldest = oldest is { } current && current > high ? current : high;
            }
            if (empty.TryGetValue(quantity, out var emptyCount)) notes.Add($"{emptyCount} of {facilities} no value yet");
            if (missing.TryGetValue(quantity, out var missingCount))
            {
                notes.Add($"point not found on {missingCount} of {facilities}");
                if (!ages.ContainsKey(quantity) && !empty.ContainsKey(quantity) && missingCount == facilities) broken.Add(quantity);
            }
            if (notes.Count > 0) parts.Add($"{quantity} {string.Join(", ", notes)}");
        }
        var summary = $"Sampled {facilities} facility(ies): " + string.Join("; ", parts) + "." + (partial ? " (Sampling stopped at its time limit.)" : "");
        if (oldest is { } age && age > TimeSpan.FromHours(1))
            summary += $" Some data is old. A module's maximum data age must be at least {(long)Math.Ceiling(age.TotalSeconds)} seconds " +
                "to accept it; otherwise point the connection at fresher points with the point mapping.";
        return new CygNetFreshnessReport(summary, oldest, broken);
    }
}
