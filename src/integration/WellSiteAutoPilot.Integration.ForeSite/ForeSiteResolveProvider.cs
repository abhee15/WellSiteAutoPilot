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

/// <summary>Catalog, Resolve and Read over live ForeSite wells.</summary>
public sealed class ForeSiteResolveProvider(ForeSiteSession session) : IAssetCatalogProvider, IResolveProvider
{
    public async Task<ForeSiteWellSnapshot> LoadAsync(string wellId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(wellId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var well = await FetchAsync(() => session.GetAsync($"Wells/Well/{id}", cancellationToken), cancellationToken);
        var surveillance = await FetchAsync(() => session.GetAsync($"Surveillance/well/{id}", cancellationToken), cancellationToken);
        var welltest = await FetchAsync(() => session.PostAsync($"WellTests/GetLatestWellTestData/{id}", new { }, cancellationToken), cancellationToken);
        return new ForeSiteWellSnapshot(well, surveillance, welltest);
    }

    // A source that is missing or fails is an empty source, so the other sources still resolve.
    private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(
        Func<Task<HttpResponseMessage>> call, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await call();
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
