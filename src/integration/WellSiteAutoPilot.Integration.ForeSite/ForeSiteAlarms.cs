using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>An active alarm on a well. Read-only; ForeSite owns acknowledgement and suppression.</summary>
public sealed record ForeSiteAlarm(long Id, int TypeId, string Name, DateTimeOffset StartedUtc, decimal? NumericValue,
    string? StringValue, bool Deviation, bool Suppressed);

/// <summary>Read-only active alarms from <c>Alarms/CurrentAlarms/{wellId}</c>, including intelligent (pattern/statistics) alarms.</summary>
public static class ForeSiteAlarms
{
    public static async Task<IReadOnlyList<ForeSiteAlarm>> ReadAsync(ForeSiteSession session, string externalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        using var response = await session.GetAsync("Alarms/CurrentAlarms/" + wellId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var result = new List<ForeSiteAlarm>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Id", out var id) || !id.TryGetInt64(out var alarmId) ||
                    !item.TryGetProperty("AlarmTypeId", out var typeId) || !typeId.TryGetInt32(out var type) ||
                    !item.TryGetProperty("StartTime", out var start) || start.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(start.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started))
                    continue;
                var kind = item.TryGetProperty("AlarmType", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
                var name = kind.ValueKind == JsonValueKind.Object && kind.TryGetProperty("AlarmType", out var n) && n.ValueKind == JsonValueKind.String &&
                    n.GetString() is { Length: > 0 and <= 128 } text ? text : "Alarm " + type.ToString(CultureInfo.InvariantCulture);
                result.Add(new(alarmId, type, name, started.ToUniversalTime(),
                    item.TryGetProperty("NumericValue", out var num) && num.ValueKind == JsonValueKind.Number && num.TryGetDecimal(out var value) ? value : null,
                    item.TryGetProperty("StringValue", out var str) && str.ValueKind == JsonValueKind.String ? str.GetString() : null,
                    kind.ValueKind == JsonValueKind.Object && kind.TryGetProperty("IsDevationAlarm", out var dev) && dev.ValueKind == JsonValueKind.True,
                    kind.ValueKind == JsonValueKind.Object && kind.TryGetProperty("IsAlarmManuallySuppressed", out var sup) && sup.ValueKind == JsonValueKind.True));
            }
            return result;
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }
}
