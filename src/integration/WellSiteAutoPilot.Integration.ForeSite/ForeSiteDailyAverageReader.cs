using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public sealed class ForeSiteDailyAverageReader(ForeSiteSession session)
{
    private const string Quantity = ForeSiteDailyAverageContract.Quantity;
    private const string Source = "foresite:daily-averages";
    private const int MaximumRows = 64;
    private const string RequestTimeFormat = "yyyy-MM-dd'T'HH:mm:ssK";

    public static readonly AdapterOperationDescriptor Operation = new(
        ForeSiteDailyAverageContract.OperationKey, "Daily inferred total fluid rate", "Read ForeSite production-day values.",
        ProviderCapabilityKind.ExtensionRead, "foresite.daily-average", ForeSiteDailyAverageContract.Version,
        new("foresite.daily-average-request/v1", [
            new("assetExternalId", "string", true, "Imported numeric ForeSite well ID."),
            new("startUtc", "date-time", true, "Inclusive production-day start filter, with an explicit offset and whole seconds."),
            new("endUtc", "date-time", true, "Exclusive production-day start filter, with an explicit offset and whole seconds."),
            new("quantity", "string", false, "The daily field to read per day. Omit for the total inferred fluid rate.")]),
        new("foresite.daily-average-result/v1", [
            new("assetExternalId", "string", true, "Requested ForeSite well ID."),
            new("quantity", "string", true, "ForeSite total inferred fluid rate."),
            new("unit", "string|null", false, "UnitKey supplied by ForeSite."),
            new("startUtc", "date-time", true, "Requested start instant normalized to UTC."),
            new("endUtc", "date-time", true, "Requested end instant normalized to UTC."),
            new("days", "object[]", true, "Records ordered by production-day start. Null values remain null."),
            new("days[].id", "string", true, "ForeSite record ID."),
            new("days[].start", "date-time", true, "Source StartDateTime, with its offset."),
            new("days[].end", "date-time", true, "Source EndDateTime, with its offset."),
            new("days[].value", "decimal|null", false, "Provider-reported value when present."),
            new("observedQuantities", "object[]", false, "Additional inferred phase-rate fields with numeric values and source units in the window."),
            new("retrievedAtUtc", "date-time", true, "Separate retrieval instant."),
            new("source", "string", true, "ForeSite daily-average provenance.")]),
        "Whole source production days selected by StartDateTime in [start, end). Boundaries and offsets are preserved.",
        "The daily record supplies no verified telemetry quality; missing values remain null.",
        new(1, 4096, 32768, TimeoutSeconds: 6, MaximumWindowDays: HistoricalReadLimits.MaximumWindowDays,
            MaximumSamples: MaximumRows, MaximumSourceBytes: HistoricalReadLimits.MaximumSourceBytes),
        ResourceKind: ForeSiteAdapter.WellResourceKind,
        Documentation: new("daily.inferred-total-fluid-rate", "Server-authorized read through the Integration Gateway.",
            """{"assetExternalId":"101","startUtc":"2026-11-01T00:00:00Z","endUtc":"2026-11-03T00:00:00Z"}""",
            """{"assetExternalId":"101","quantity":"TotalFluidRateInferred","unit":"STB/d","startUtc":"2026-11-01T00:00:00Z","endUtc":"2026-11-03T00:00:00Z","days":[{"id":"123","start":"2026-11-01T05:00:00Z","end":"2026-11-02T05:59:59.9900000Z","value":420}],"retrievedAtUtc":"2026-11-15T12:00:00Z","source":"foresite:daily-averages"}""",
            "GET Surveillance/Well/DailyAverage/{wellId}/{start}/{end}."));

    public static bool IsValidRequest(ForeSiteDailyAverageRequest request) =>
        TryReadRequest(request, out _, out _, out _);

    public async Task<ForeSiteDailyAverageResult> ReadAsync(ForeSiteDailyAverageRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadRequest(request, out var wellId, out var from, out var to))
            throw new ForeSiteMeasurementException("FORESITE_DAILY_REQUEST_INVALID");

        static string RouteTime(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        using var response = await session.GetAsync(
            $"Surveillance/Well/DailyAverage/{wellId.ToString(CultureInfo.InvariantCulture)}/{RouteTime(from)}/{RouteTime(to)}",
            cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new ForeSiteMeasurementException("FORESITE_DAILY_READ_FAILED");
        if (response.Content.Headers.ContentLength > HistoricalReadLimits.MaximumSourceBytes)
            throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");

        try
        {
            await response.Content.LoadIntoBufferAsync(HistoricalReadLimits.MaximumSourceBytes, cancellationToken);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            var projected = Project(document.RootElement, request.AssetExternalId, wellId, from, to, cancellationToken);
            return request.Quantity is null or Quantity ? projected : ForQuantity(projected, request.Quantity);
        }
        catch (HttpRequestException) { throw new ForeSiteMeasurementException("FORESITE_DAILY_BODY_INVALID"); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static ForeSiteDailyAverageResult Project(JsonElement root, string externalId, long wellId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ValidateObject(root);
        if (root.TryGetProperty("FullErrorMessage", out var error) && error.ValueKind != JsonValueKind.Null &&
            (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString())))
            throw new ForeSiteMeasurementException("FORESITE_DAILY_READ_FAILED");
        if (!root.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Array)
            throw InvalidResponse();
        if (values.GetArrayLength() > MaximumRows)
            throw new ForeSiteMeasurementException("FORESITE_DAILY_ROW_LIMIT");

        var days = new List<ForeSiteDailyAverageRecord>();
        var ids = new HashSet<long>();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in values.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateObject(row);
            if (PositiveId(row, "WellId") != wellId)
                throw new ForeSiteMeasurementException("FORESITE_DAILY_WELL_MISMATCH");
            var id = PositiveId(row, "Id");
            var start = ReadTime(row, "StartDateTime");
            var end = ReadTime(row, "EndDateTime");
            if (end <= start || !ids.Add(id)) throw InvalidResponse();
            decimal? value = null;
            if (row.TryGetProperty(Quantity, out var field) && field.ValueKind != JsonValueKind.Null)
            {
                if (field.ValueKind != JsonValueKind.Number || !field.TryGetDecimal(out var number)) throw InvalidResponse();
                value = number;
            }
            var populated = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var property in row.EnumerateObject())
                if (ForeSiteDailyAverageContract.IsObservableQuantityName(property.Name) &&
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var populatedValue) &&
                    populated.Count < ForeSiteDailyAverageContract.MaximumObservedQuantities)
                    populated[property.Name] = populatedValue;
            days.Add(new(id.ToString(CultureInfo.InvariantCulture), start, end, value) { Values = populated });
            if (start >= from && start < to)
                foreach (var name in populated.Keys)
                    if (name != Quantity) observed.Add(name);
        }
        days.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var i = 1; i < days.Count; i++)
            if (days[i].Start < days[i - 1].End) throw InvalidResponse();

        var unit = ReadUnit(root);
        var selected = days.Where(day => day.Start >= from && day.Start < to).ToArray();
        if (unit is null && selected.Any(day => day.Value is not null))
            throw new ForeSiteMeasurementException("FORESITE_DAILY_UNIT_MISSING");
        return new(externalId, Quantity, unit, from.ToUniversalTime(), to.ToUniversalTime(), selected, DateTimeOffset.UtcNow, Source)
        {
            ObservedQuantities = ReadObservedQuantities(root, observed),
            UnitsByQuantity = ReadFieldUnits(root, selected)
        };
    }

    /// <summary>The same production days, each carrying the value of another populated provider field and that field's unit.</summary>
    private static ForeSiteDailyAverageResult ForQuantity(ForeSiteDailyAverageResult result, string quantity)
    {
        var days = result.Days.Select(day => day with
        {
            Value = day.Values is not null && day.Values.TryGetValue(quantity, out var value) ? value : null
        }).ToArray();
        string? unit = null;
        var catalogued = ForeSiteDailyCatalogue.Find(quantity);
        var hasValue = days.Any(day => day.Value is not null);
        if (hasValue && catalogued?.Unit == ForeSiteDailyQuantity.CountUnit)
            unit = catalogued.Unit;
        else if (hasValue && result.UnitsByQuantity?.TryGetValue(quantity, out unit) != true)
            throw new ForeSiteMeasurementException("FORESITE_DAILY_UNIT_MISSING");
        // A different unit system is refused rather than converted, as for surveillance values.
        if (hasValue && catalogued is not null && unit != catalogued.Unit)
            throw new ForeSiteMeasurementException("FORESITE_UNIT_MISMATCH");
        return result with { Quantity = quantity, Unit = unit, Days = days, ObservedQuantities = null };
    }

    private static string? ReadUnit(JsonElement root)
    {
        if (!root.TryGetProperty("Units", out var units) || units.ValueKind == JsonValueKind.Null) return null;
        ValidateObject(units);
        if (!units.TryGetProperty(Quantity, out var unit) || unit.ValueKind == JsonValueKind.Null) return null;
        ValidateObject(unit);
        if (!unit.TryGetProperty("UnitKey", out var key) || key.ValueKind == JsonValueKind.Null) return null;
        return UnitText(key) ?? throw InvalidResponse();
    }

    private static List<ForeSiteDailyObservedQuantity> ReadObservedQuantities(JsonElement root, HashSet<string> observed)
    {
        // ReadUnit has already checked the Units object. Malformed optional phase units cannot invalidate the total rate.
        var quantities = new List<ForeSiteDailyObservedQuantity>(observed.Count);
        if (root.TryGetProperty("Units", out var units) && units.ValueKind == JsonValueKind.Object)
            foreach (var name in observed.Order(StringComparer.Ordinal))
                if (quantities.Count < ForeSiteDailyAverageContract.MaximumObservedQuantities &&
                    units.TryGetProperty(name, out var unit) && unit.ValueKind == JsonValueKind.Object &&
                    unit.EnumerateObject().Count(field => field.Name.Equals("UnitKey", StringComparison.OrdinalIgnoreCase)) == 1 &&
                    unit.TryGetProperty("UnitKey", out var key) && UnitText(key) is { } text)
                    quantities.Add(new(name, text));
        return quantities;
    }

    private static Dictionary<string, string> ReadFieldUnits(JsonElement root, IEnumerable<ForeSiteDailyAverageRecord> days)
    {
        // Lenient by design: a field with no usable unit is simply absent, and a read of that field fails on its own.
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("Units", out var all) || all.ValueKind != JsonValueKind.Object) return units;
        foreach (var name in days.SelectMany(day => day.Values?.Keys ?? []).Distinct(StringComparer.Ordinal))
            if (all.TryGetProperty(name, out var unit) && unit.ValueKind == JsonValueKind.Object &&
                unit.EnumerateObject().Count(field => field.Name.Equals("UnitKey", StringComparison.OrdinalIgnoreCase)) == 1 &&
                unit.TryGetProperty("UnitKey", out var key) && UnitText(key) is { } text)
                units[name] = text;
        return units;
    }

    private static string? UnitText(JsonElement key) =>
        key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 and <= 32 } text &&
        !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl) ? text : null;

    private static void ValidateObject(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in item.EnumerateObject())
            if (!fields.Add(field.Name)) throw InvalidResponse();
    }

    private static long PositiveId(JsonElement row, string name) =>
        row.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0
            ? number : throw InvalidResponse();

    private static DateTimeOffset ReadTime(JsonElement row, string name) =>
        row.TryGetProperty(name, out var time) && time.ValueKind == JsonValueKind.String &&
        HasOffset(time.GetString()) && time.TryGetDateTimeOffset(out var value) && value != default
            ? value : throw InvalidResponse();

    private static bool HasOffset(string? text) => text is { Length: >= 20 } &&
        (text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':');

    private static bool TryReadRequest(ForeSiteDailyAverageRequest request, out long id, out DateTimeOffset from, out DateTimeOffset to)
    {
        id = 0;
        from = to = default;
        return request.AssetExternalId is { Length: > 0 and <= 20 } &&
            long.TryParse(request.AssetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0 &&
            (request.Quantity is null || ForeSiteDailyAverageContract.IsObservableQuantityName(request.Quantity)) &&
            HasOffset(request.StartUtc) && HasOffset(request.EndUtc) &&
            DateTimeOffset.TryParseExact(request.StartUtc, RequestTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out from) &&
            DateTimeOffset.TryParseExact(request.EndUtc, RequestTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out to) &&
            from != default && to > from && to - from <= TimeSpan.FromDays(HistoricalReadLimits.MaximumWindowDays);
    }

    private static ForeSiteMeasurementException InvalidResponse() => new("FORESITE_DAILY_RESPONSE_INVALID");
}
