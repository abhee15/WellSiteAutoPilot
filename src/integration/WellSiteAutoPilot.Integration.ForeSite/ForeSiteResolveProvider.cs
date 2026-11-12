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
    /// <summary>Units ForeSite reports for surveillance fields (for example psia), keyed by field name.</summary>
    public IReadOnlyDictionary<string, string> SurveillanceUnits { get; init; } = new Dictionary<string, string>();

    /// <summary>Units ForeSite reports for the well test fields (for example psia), keyed by field name.</summary>
    public IReadOnlyDictionary<string, string> WellTestUnits { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> UnitsOf(string endpoint) => endpoint switch
    {
        "surveillance" => SurveillanceUnits,
        "welltest" => WellTestUnits,
        _ => new Dictionary<string, string>()
    };

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
        ["PumpSpm"] = [new("surveillance", "CurrentSPM", ResolveBasis.Current, "spm")],
        ["PumpFillage"] =
        [
            new("surveillance", "CurrentPumpFillage", ResolveBasis.Current, "%"),
            new("surveillance", "AveragePumpFillage", ResolveBasis.DailyAverage, "%")
        ]
    };

    public const string SurveillancePrefix = "surveillance:";
    public const string SetPointPrefix = "setpoint:";

    /// <summary>Logical inputs that are controller set points. ForeSite keeps only saved values for these.</summary>
    public static bool IsSetPointInput(string input) =>
        input.StartsWith(SetPointPrefix, StringComparison.OrdinalIgnoreCase) || input.Equals("IdleTime", StringComparison.OrdinalIgnoreCase);

    public static string SetPointName(string input) =>
        input.StartsWith(SetPointPrefix, StringComparison.OrdinalIgnoreCase) ? input[SetPointPrefix.Length..] : input;

    /// <summary>Decides whether a saved set point can answer an input. Pure, so it tests without ForeSite.</summary>
    public static ResolveResult ResolveSetPoint(string input, string json)
    {
        var name = SetPointName(input);
        var found = ForeSiteSetPoints.Parse(json, includeReadOnly: true).FirstOrDefault(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase));
        if (found.Id is null)
            return ResolveResult.Failed(ResolveOutcome.NotApplicable, $"This well has no saved set point named {name}.");
        if (!decimal.TryParse(found.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return ResolveResult.Failed(ResolveOutcome.Misconfigured, $"Set point {found.Id} is not numeric (\"{found.Value}\").");
        return ResolveResult.Compatible(new ResolveCandidate($"foresite:setpoint.{found.Id}", ResolveBasis.Saved,
            $"saved set point {found.Id}" + (found.Unit.Length > 0 ? $" ({found.Unit})" : ""), $"setpoint|{found.Id}|{found.Unit}"));
    }

    public const string WellTestPrefix = "welltest:";
    public const string WellPrefix = "well:";

    public static IReadOnlyCollection<string> KnownInputs => Known.Keys;

    /// <summary>The basis a raw point implies: well test values are samples, "Average" fields are daily averages, the rest are current.</summary>
    public static ResolveBasis BasisOf(string endpoint, string field) =>
        endpoint == "welltest" ? ResolveBasis.TestSample
        : endpoint == "well" ? ResolveBasis.Static
        : field.Contains("Average", StringComparison.OrdinalIgnoreCase) ? ResolveBasis.DailyAverage
        : ResolveBasis.Current;

    private static Source? PointSource(string input)
    {
        foreach (var (prefix, endpoint) in new[] { (SurveillancePrefix, "surveillance"), (WellTestPrefix, "welltest"), (WellPrefix, "well") })
            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && input.Length > prefix.Length)
            {
                var field = input[prefix.Length..];
                return new Source(endpoint, field, BasisOf(endpoint, field), "");
            }
        return null;
    }

    public static ResolveResult Resolve(string input, ForeSiteWellSnapshot snapshot, ResolveBasis? basis = null)
    {
        Source[] sources;
        if (PointSource(input) is { } point)
            sources = [point];
        else if (!Known.TryGetValue(input, out sources!))
            return ResolveResult.Failed(ResolveOutcome.Misconfigured,
                $"'{input}' is not mapped for ForeSite. Mapped inputs: {string.Join(", ", Known.Keys.Order())}, or '{SurveillancePrefix}<point>', 'welltest:<point>' or 'well:<point>'.");

        var found = new List<ResolveCandidate>();
        var reasons = new List<string>();
        foreach (var source in sources)
        {
            if (basis is not null && source.Basis != basis)
                continue;
            var unit = snapshot.UnitsOf(source.Endpoint).TryGetValue(source.Field, out var reported) ? reported : source.Unit;
            if (snapshot.Source(source.Endpoint).TryGetValue(source.Field, out var raw) && !string.IsNullOrWhiteSpace(raw))
                found.Add(new ResolveCandidate($"foresite:{source.Endpoint}.{source.Field}", source.Basis,
                    $"{source.Endpoint}.{source.Field}" + (unit.Length > 0 ? $" ({unit})" : ""),
                    $"{source.Endpoint}|{source.Field}|{unit}"));
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

    /// <summary>
    /// When ForeSite says a value was observed: the controller's last good scan for surveillance values, the end of the
    /// test (else the sample date) of the well test row for test samples. Only timestamps with an explicit UTC offset are
    /// trusted, as in the production adapter; anything else is unknown, and the caller must not present retrieval time
    /// as observation time. The surveillance LatestValidWellTestDate is a different test and is not used.
    /// </summary>
    public static (DateTimeOffset? At, string Kind) ObservedAt(string endpoint, ForeSiteWellSnapshot snapshot)
    {
        var (fieldNames, kind) = endpoint switch
        {
            "surveillance" => (new[] { "LastGoodScanTime" }, "ControllerScan"),
            "welltest" => (new[] { "TestEndDate", "SampleDate" }, "WellTestDate"),
            _ => (Array.Empty<string>(), "Static")
        };
        if (fieldNames.Length == 0)
            return (null, kind);
        var fields = snapshot.Source(endpoint);
        foreach (var field in fieldNames)
            if (fields.TryGetValue(field, out var text) && ForeSiteSurveillanceSnapshot.TryOffsetTime(text, out var at))
                return (at, kind);
        return (null, "Unknown");
    }

    /// <summary>The unit ForeSite reports per surveillance field, from the "Units" block that comes with the values.</summary>
    public static IReadOnlyDictionary<string, string> Units(JsonElement root)
    {
        var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Units", out var block) && block.ValueKind == JsonValueKind.Object)
            foreach (var property in block.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("UnitKey", out var key)
                    && key.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString()))
                    units[property.Name] = key.GetString()!;
        return units;
    }

    /// <summary>Top-level scalar fields of a JSON object (or of its "Value" object); nulls and nested objects are skipped.</summary>
    public static IReadOnlyDictionary<string, string> Flatten(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Value", out var inner) && inner.ValueKind == JsonValueKind.Object)
            root = inner;
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Values", out var rows) && rows.ValueKind == JsonValueKind.Array
            && rows.GetArrayLength() > 0 && rows[0].ValueKind == JsonValueKind.Object)
            root = rows[0];
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
    public static IReadOnlyList<(string Id, string Description, string Unit, string Value, decimal? Low, decimal? High)> Parse(string json, bool includeReadOnly = false)
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
                if (!includeReadOnly && Flag(p, "IsReadOnly") || Flag(p, "IsHidden"))
                    continue;
                list.Add((Text(p, "ElementId"), Text(p, "Description"), Text(p, "UnitKey"), Text(p, "OriginalValue"),
                    Number(p, "RangeLow"), Number(p, "RangeHigh")));
            }
        }
        return list;
    }

    /// <param name="absolute">When given, the proposed value is exactly this and <paramref name="delta"/> is ignored.</param>
    public static SetPointWritePlan Plan(string wellId, string json, string setPoint, decimal delta, decimal? absolute = null)
    {
        var found = Parse(json).Where(p => string.Equals(p.Id, setPoint, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0)
            return new SetPointWritePlan(wellId, setPoint, "", "", 0, 0, null, null, false, $"'{setPoint}' is not a writable set point on this well.");
        var p = found[0];
        if (!decimal.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            return new SetPointWritePlan(wellId, p.Id, p.Description, p.Unit, 0, 0, p.Low, p.High, false, $"'{p.Id}' is not numeric ('{p.Value}').");
        var proposed = absolute ?? current + delta;
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
        var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var surveillance = await FetchAsync(() => session.GetAsync($"Surveillance/well/{id}", cancellationToken), cancellationToken, units);
        // The latest valid test comes with its units (psia); the latest test of any kind does not, so it is only the fallback.
        var testUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var welltest = await FetchAsync(() => session.GetAsync($"WellTests/LatestValidWellTest/{id}", cancellationToken), cancellationToken, testUnits);
        if (welltest.Count == 0)
            welltest = await FetchAsync(() => session.GetAsync($"WellTests/GetLatestWellTestData/{id}", cancellationToken), cancellationToken);
        return new ForeSiteWellSnapshot(well, surveillance, welltest) { SurveillanceUnits = units, WellTestUnits = testUnits };
    }

    // A source that is missing or fails is an empty source, so the other sources still resolve. ForeSite closes idle
    // connections, so a reset is retried once on a fresh connection.
    private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(
        Func<Task<HttpResponseMessage>> call, CancellationToken cancellationToken, Dictionary<string, string>? units = null)
    {
        try
        {
            using var response = await Retry(call);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, string>();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (units is not null)
                foreach (var (field, unit) in ForeSiteResolver.Units(document.RootElement))
                    units[field] = unit;
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

    /// <summary>Every point ForeSite holds a value for on this well, from surveillance, the latest well test and the well record.</summary>
    public async Task<IReadOnlyList<CatalogPoint>> GetPointsAsync(string assetExternalId, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(assetExternalId, cancellationToken);
        var points = new List<CatalogPoint>();
        foreach (var (endpoint, prefix, group) in new[]
            { ("surveillance", ForeSiteResolver.SurveillancePrefix, "Surveillance"), ("welltest", ForeSiteResolver.WellTestPrefix, "Well test"), ("well", ForeSiteResolver.WellPrefix, "Well") })
            foreach (var p in snapshot.Source(endpoint).Where(p => !string.IsNullOrWhiteSpace(p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal))
                points.Add(new CatalogPoint(prefix + p.Key, p.Value.Trim(), group, ForeSiteResolver.BasisOf(endpoint, p.Key).ToString()));
        return points;
    }

    public async ValueTask<ResolveResult> ResolveAsync(
        string assetExternalId, string input, ResolveBasis? basis = null, CancellationToken cancellationToken = default) =>
        ForeSiteResolver.IsSetPointInput(input)
            ? (basis is null or ResolveBasis.Saved
                ? ForeSiteResolver.ResolveSetPoint(input, await SavedSetPointsAsync(assetExternalId, cancellationToken))
                : ResolveResult.Failed(ResolveOutcome.NotApplicable, $"ForeSite only holds saved values for set point {input}, not a {basis} reading."))
            : ForeSiteResolver.Resolve(input, await LoadAsync(assetExternalId, cancellationToken), basis);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, string Json)> SetPointJson = new();

    /// <summary>The slow set point read is kept for a minute so a resolve followed by a read asks ForeSite once.</summary>
    private async Task<string> SavedSetPointsAsync(string wellId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(wellId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var key = cacheScope + "|" + id;
        if (SetPointJson.TryGetValue(key, out var kept) && DateTimeOffset.UtcNow - kept.At < TimeSpan.FromSeconds(60))
            return kept.Json;
        using var response = await Retry(() => session.GetAsync($"Surveillance/Well/AllSavedSetPointParams?WellId={id}&scanOption=0", cancellationToken));
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_SETPOINTS_UNAVAILABLE");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        SetPointJson[key] = (DateTimeOffset.UtcNow, json);
        return json;
    }

    /// <summary>Dry-run only: reads the saved set point and plans the change. Sending is a separate, approved step.</summary>
    public async Task<SetPointWritePlan> PlanSetPointChangeAsync(
        string wellId, string setPoint, decimal delta, decimal? absolute = null, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(wellId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        using var response = await Retry(() => session.GetAsync(
            $"Surveillance/Well/AllSavedSetPointParams?WellId={id}&scanOption=0", cancellationToken));
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_SETPOINTS_UNAVAILABLE");
        return ForeSiteSetPoints.Plan(wellId, await response.Content.ReadAsStringAsync(cancellationToken), setPoint, delta, absolute);
    }

    public async ValueTask<EngineeringValue> ReadAsync(
        string assetExternalId, ResolveCandidate candidate, CancellationToken cancellationToken = default)
    {
        var parts = candidate.ReadPlan?.Split('|') ?? throw new ForeSiteMeasurementException("FORESITE_READ_PLAN_INVALID");
        if (parts[0] == "setpoint")
        {
            var saved = ForeSiteSetPoints.Parse(await SavedSetPointsAsync(assetExternalId, cancellationToken), includeReadOnly: true)
                .FirstOrDefault(p => string.Equals(p.Id, parts[1], StringComparison.OrdinalIgnoreCase));
            if (saved.Id is null || !decimal.TryParse(saved.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stored))
                throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
            var now = DateTimeOffset.UtcNow;
            // The saved value has no observation time and may differ from the controller, so it is never reported as Good.
            return new EngineeringValue(saved.Id, stored, parts[2], now, "Uncertain", candidate.Source, now, "SavedValue");
        }
        var snapshot = await LoadAsync(assetExternalId, cancellationToken);
        if (!snapshot.Source(parts[0]).TryGetValue(parts[1], out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
        var (observed, kind) = ForeSiteResolver.ObservedAt(parts[0], snapshot);
        var retrieved = DateTimeOffset.UtcNow;
        // Without a trustworthy observation time the value is Uncertain, never Good, so a freshness rule cannot pass on it.
        var quality = observed is null && parts[0] != "well" ? "Uncertain" : "Good";
        return new EngineeringValue(parts[1], number, parts[2], observed ?? retrieved, quality,
            candidate.Source, retrieved, kind);
    }
}
