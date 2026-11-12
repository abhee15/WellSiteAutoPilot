using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>The scalar fields ForeSite returned for one well, grouped by the endpoint they came from.</summary>
public sealed record ForeSiteWellSnapshot(
    IReadOnlyDictionary<string, string> Well,
    IReadOnlyDictionary<string, string> Surveillance,
    IReadOnlyDictionary<string, string> WellTest)
{
    public IReadOnlyDictionary<string, string> Source(string name) => name switch
    {
        "well" => Well,
        "surveillance" => Surveillance,
        "welltest" => WellTest,
        _ => new Dictionary<string, string>()
    };
}

/// <summary>
/// Decides which ForeSite sources can supply an input on one well. One input can come from several places
/// (casing pressure: the controller scan or the latest well test); each is a candidate with its own basis.
/// The decision is a pure function of the snapshot so it tests without ForeSite.
/// </summary>
public static class ForeSiteResolver
{
    private sealed record Source(string Endpoint, string Field, ResolveBasis Basis, string Unit);

    private static readonly Dictionary<string, Source[]> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Latitude"] = [new("well", "SurfaceLatitude", ResolveBasis.Static, "deg")],
        ["Longitude"] = [new("well", "SurfaceLongitude", ResolveBasis.Static, "deg")],
        ["CasingPressure"] =
        [
            new("surveillance", "CasingPressure", ResolveBasis.Current, "psi"),
            new("welltest", "AverageCasingPressure", ResolveBasis.TestSample, "psi")
        ],
        ["TubingPressure"] =
        [
            new("surveillance", "TubingPressure", ResolveBasis.Current, "psi"),
            new("welltest", "AverageTubingPressure", ResolveBasis.TestSample, "psi")
        ],
        ["PumpFillage"] =
        [
            new("surveillance", "CurrentPumpFillage", ResolveBasis.Current, "%"),
            new("surveillance", "AveragePumpFillage", ResolveBasis.DailyAverage, "%")
        ]
    };

    public const string SurveillancePrefix = "surveillance:";

    public static IReadOnlyCollection<string> KnownInputs => Known.Keys;

    public static ResolveResult Resolve(string input, ForeSiteWellSnapshot snapshot, ResolveBasis? basis = null)
    {
        Source[] sources;
        if (input.StartsWith(SurveillancePrefix, StringComparison.OrdinalIgnoreCase))
            sources = [new("surveillance", input[SurveillancePrefix.Length..], ResolveBasis.Current, "")];
        else if (!Known.TryGetValue(input, out sources!))
            return ResolveResult.Failed(ResolveOutcome.Misconfigured,
                $"'{input}' is not mapped for ForeSite. Mapped inputs: {string.Join(", ", Known.Keys.Order())}, or '{SurveillancePrefix}<point>'.");

        var found = new List<ResolveCandidate>();
        var reasons = new List<string>();
        foreach (var source in sources)
        {
            if (basis is not null && source.Basis != basis)
                continue;
            if (snapshot.Source(source.Endpoint).TryGetValue(source.Field, out var raw) && !string.IsNullOrWhiteSpace(raw))
                found.Add(new ResolveCandidate($"foresite:{source.Endpoint}.{source.Field}", source.Basis,
                    $"{source.Endpoint}.{source.Field}" + (source.Unit.Length > 0 ? $" ({source.Unit})" : ""),
                    $"{source.Endpoint}|{source.Field}|{source.Unit}"));
            else
                reasons.Add($"{source.Endpoint} has no value for {source.Field} on this well.");
        }

        if (found.Count == 0)
        {
            if (reasons.Count == 0)
                reasons.Add($"ForeSite has no '{basis}' source for '{input}'.");
            return ResolveResult.Failed(ResolveOutcome.NotApplicable, [.. reasons]);
        }
        return found.Count == 1 ? ResolveResult.Compatible(found[0])
            : new ResolveResult(ResolveOutcome.NeedsSelection, found,
                [$"'{input}' is available from {found.Count} sources; the services team must pick a basis."]);
    }

    /// <summary>Whether a value can be written back. ForeSite writes reach the field controller only through its set points.</summary>
    public static ResolveResult ResolveCommand(string input, IReadOnlyCollection<string> writableSetPoints)
    {
        var name = input.StartsWith("setpoint:", StringComparison.OrdinalIgnoreCase) ? input["setpoint:".Length..] : input;
        if (writableSetPoints.Contains(name, StringComparer.OrdinalIgnoreCase))
            return ResolveResult.Compatible(new ResolveCandidate($"foresite:setpoint.{name}", ResolveBasis.Current, $"set point {name}", name));
        return ResolveResult.Failed(ResolveOutcome.NotApplicable,
            $"'{input}' is not a writable set point on this well, so it cannot be sent to the controller. Writable: {string.Join(", ", writableSetPoints.Order())}.");
    }

    /// <summary>Top-level scalar fields of a JSON object (or of its "Value" object); nulls and nested objects are skipped.</summary>
    public static IReadOnlyDictionary<string, string> Flatten(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Value", out var inner) && inner.ValueKind == JsonValueKind.Object)
            root = inner;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
            return fields;
        foreach (var property in root.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()! : property.Value.GetRawText();
        return fields;
    }
}

public static class ForeSiteSetPoints
{
    /// <summary>Writable set points (not read-only, not hidden) from an AllSavedSetPointParams response.</summary>
    public static IReadOnlyList<(string Id, string Description, string Unit, string Value, decimal? Low, decimal? High)> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var list = new List<(string, string, string, string, decimal?, decimal?)>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var group in document.RootElement.EnumerateArray())
        {
            if (!group.TryGetProperty("SetPointsList", out var points) || points.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var p in points.EnumerateArray())
            {
                if (Flag(p, "IsReadOnly") || Flag(p, "IsHidden"))
                    continue;
                list.Add((Text(p, "ElementId"), Text(p, "Description"), Text(p, "UnitKey"), Text(p, "OriginalValue"),
                    Number(p, "RangeLow"), Number(p, "RangeHigh")));
            }
        }
        return list;
    }

    public static SetPointWritePlan Plan(string wellId, string json, string setPoint, decimal delta)
    {
        var found = Parse(json).Where(p => string.Equals(p.Id, setPoint, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0)
            return new SetPointWritePlan(wellId, setPoint, "", "", 0, 0, null, null, false, $"'{setPoint}' is not a writable set point on this well.");
        var p = found[0];
        if (!decimal.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            return new SetPointWritePlan(wellId, p.Id, p.Description, p.Unit, 0, 0, p.Low, p.High, false, $"'{p.Id}' is not numeric ('{p.Value}').");
        var proposed = current + delta;
        var inRange = (p.Low is null || proposed >= p.Low) && (p.High is null || proposed <= p.High);
        return new SetPointWritePlan(wellId, p.Id, p.Description, p.Unit, current, proposed, p.Low, p.High, inRange,
            inRange ? null : $"{proposed} is outside the allowed range {p.Low}..{p.High}.");
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
    private static decimal? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
}

/// <summary>Short-lived snapshots per well, so the several inputs of one preview or run share a single load of ForeSite.</summary>
public sealed class ForeSiteSnapshotCache(TimeSpan lifetime, TimeProvider clock)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, ForeSiteWellSnapshot Snapshot)> _items = new();

    public async Task<ForeSiteWellSnapshot> GetAsync(string key, Func<Task<ForeSiteWellSnapshot>> load)
    {
        var now = clock.GetUtcNow();
        if (_items.TryGetValue(key, out var hit) && now - hit.At < lifetime)
            return hit.Snapshot;
        var snapshot = await load();
        if (_items.Count > 512)
            _items.Clear();
        _items[key] = (now, snapshot);
        return snapshot;
    }
}

/// <summary>Catalog, Resolve and Read over live ForeSite wells.</summary>
public sealed class ForeSiteResolveProvider(ForeSiteSession session, ForeSiteSnapshotCache? cache = null, string cacheScope = "")
    : IAssetCatalogProvider, IResolveProvider
{
    public Task<ForeSiteWellSnapshot> LoadAsync(string wellId, CancellationToken cancellationToken) =>
        cache is null ? LoadFreshAsync(wellId, cancellationToken)
            : cache.GetAsync(cacheScope + "|" + wellId, () => LoadFreshAsync(wellId, cancellationToken));

    private async Task<ForeSiteWellSnapshot> LoadFreshAsync(string wellId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(wellId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var well = await FetchAsync(() => session.GetAsync($"Wells/Well/{id}", cancellationToken), cancellationToken);
        var surveillance = await FetchAsync(() => session.GetAsync($"Surveillance/well/{id}", cancellationToken), cancellationToken);
        var welltest = await FetchAsync(() => session.PostAsync($"WellTests/GetLatestWellTestData/{id}", new { }, cancellationToken), cancellationToken);
        return new ForeSiteWellSnapshot(well, surveillance, welltest);
    }

    // A source that is missing or fails is an empty source, so the other sources still resolve. ForeSite closes idle
    // connections, so a reset is retried once on a fresh connection.
    private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(
        Func<Task<HttpResponseMessage>> call, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Retry(call);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, string>();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ForeSiteResolver.Flatten(document.RootElement);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static async Task<HttpResponseMessage> Retry(Func<Task<HttpResponseMessage>> call)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await call(); }
            catch (HttpRequestException) when (attempt < 2) { await Task.Delay(300 * (attempt + 1)); }
        }
    }

    public async ValueTask<IReadOnlyList<AssetCatalogEntry>> GetCatalogAsync(
        string assetExternalId, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(assetExternalId, cancellationToken);
        return snapshot.Surveillance
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => new AssetCatalogEntry(ForeSiteResolver.SurveillancePrefix + p.Key, "surveillance point", null))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask<ResolveResult> ResolveAsync(
        string assetExternalId, string input, ResolveBasis? basis = null, CancellationToken cancellationToken = default) =>
        ForeSiteResolver.Resolve(input, await LoadAsync(assetExternalId, cancellationToken), basis);

    /// <summary>Dry-run only: reads the saved set point and plans the change. Sending is a separate, approved step.</summary>
    public async Task<SetPointWritePlan> PlanSetPointChangeAsync(
        string wellId, string setPoint, decimal delta, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(wellId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        using var response = await Retry(() => session.GetAsync(
            $"Surveillance/Well/AllSavedSetPointParams?WellId={id}&scanOption=0", cancellationToken));
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_SETPOINTS_UNAVAILABLE");
        return ForeSiteSetPoints.Plan(wellId, await response.Content.ReadAsStringAsync(cancellationToken), setPoint, delta);
    }

    public async ValueTask<EngineeringValue> ReadAsync(
        string assetExternalId, ResolveCandidate candidate, CancellationToken cancellationToken = default)
    {
        var parts = candidate.ReadPlan?.Split('|') ?? throw new ForeSiteMeasurementException("FORESITE_READ_PLAN_INVALID");
        var snapshot = await LoadAsync(assetExternalId, cancellationToken);
        if (!snapshot.Source(parts[0]).TryGetValue(parts[1], out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
        return new EngineeringValue(parts[1], number, parts[2], DateTimeOffset.UtcNow, "Good",
            candidate.Source, DateTimeOffset.UtcNow, candidate.Basis.ToString());
    }
}
