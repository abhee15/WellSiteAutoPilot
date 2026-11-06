using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>One controller setpoint as ForeSite reports it. Values are read-only here.</summary>
public sealed record ForeSiteSetpoint(string ElementId, string Description, string Group, decimal? Value, string? RawValue,
    string? Unit, decimal? RangeLow, decimal? RangeHigh, bool ReadOnly);

/// <summary>
/// Read-only setpoints from <c>Surveillance/Well/AllSavedSetPointParams</c>. ForeSite names setpoints per controller
/// (POLoadSP on a WellPilot, UMINSPM on a Unico), so the element id is returned as is and a per-device-type map
/// belongs in data, not in the logic module. Hidden elements are skipped. Non-numeric values (enums, text) keep only RawValue.
/// </summary>
public static class ForeSiteSetpoints
{
    public static async Task<IReadOnlyList<ForeSiteSetpoint>> ReadAsync(ForeSiteSession session, string externalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        using var response = await session.GetAsync(
            $"Surveillance/Well/AllSavedSetPointParams?WellId={wellId.ToString(CultureInfo.InvariantCulture)}&scanOption=0", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var result = new List<ForeSiteSetpoint>();
            foreach (var group in document.RootElement.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("SetPointsList", out var list) ||
                    list.ValueKind != JsonValueKind.Array) continue;
                var groupName = Text(group, "DataGroupName") ?? "";
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || Text(item, "ElementId") is not { } id) continue;
                    if (item.TryGetProperty("IsHidden", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                    var raw = Text(item, "Value");
                    decimal? number = raw is not null && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed : null;
                    result.Add(new(id, Text(item, "Description") ?? id, groupName, number, raw, Text(item, "UnitKey"),
                        Number(item, "RangeLow"), Number(item, "RangeHigh"),
                        item.TryGetProperty("IsReadOnly", out var ro) && ro.ValueKind == JsonValueKind.True));
                }
            }
            return result;
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 and <= 128 } text
            ? text : null;

    private static decimal? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var value) ? value : null;
}
