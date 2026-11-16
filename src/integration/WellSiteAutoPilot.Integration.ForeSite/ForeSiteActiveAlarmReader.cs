using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public sealed record ForeSiteActiveAlarmRequest([property: JsonRequired] string AssetExternalId);
public sealed record ForeSiteActiveAlarm(string Id, int TypeId, string? Name, DateTimeOffset StartedAt,
    decimal? NumericValue, string? StringValue, bool? IsDeviation, bool? IsManuallySuppressed);
public sealed record ForeSiteActiveAlarmResult(string AssetExternalId, IReadOnlyList<ForeSiteActiveAlarm> Alarms,
    DateTimeOffset RetrievedAtUtc, string Source);

public sealed class ForeSiteActiveAlarmReader(ForeSiteSession session)
{
    private const int MaximumAlarms = 64;
    private const int MaximumSourceBytes = 1048576;

    public static readonly AdapterOperationDescriptor Operation = new(
        "alarms.active-well", "Active well alarms", "Read ForeSite's current alarm records for one well.",
        ProviderCapabilityKind.Alarms, "foresite.active-well-alarms", "1.0",
        new("foresite.active-well-alarm-request/v1", [new("assetExternalId", "string", true, "Imported numeric ForeSite well ID.")]),
        new("foresite.active-well-alarm-result/v1", [
            new("assetExternalId", "string", true, "Exact requested well ID."),
            new("alarms", "object[]", true, "Current alarm records in provider order."),
            new("alarms[].id", "string", true, "Source alarm ID."),
            new("alarms[].typeId", "integer", true, "Source alarm type ID."),
            new("alarms[].name", "string|null", false, "Name from the provider alarm type."),
            new("alarms[].startedAt", "date-time", true, "ForeSite StartTime with its source offset."),
            new("alarms[].numericValue", "decimal|null", false, "Numeric alarm value if supplied."),
            new("alarms[].stringValue", "string|null", false, "Text alarm value if supplied."),
            new("alarms[].isDeviation", "boolean|null", false, "Provider alarm type deviation flag."),
            new("alarms[].isManuallySuppressed", "boolean|null", false, "Provider alarm type manual suppression flag."),
            new("retrievedAtUtc", "date-time", true, "Separate retrieval instant."),
            new("source", "string", true, "ForeSite current alarm provenance.")]),
        "Alarm start time and retrieval time are independent. A current alarm can have an old start time.",
        "Current membership comes from the provider query. Suppression and deviation are independent fields. Missing flags stay null.",
        new(1, 4096, MeasurementPayloadLimits.MaximumReplyBytes, TimeoutSeconds: 6, MaximumSamples: MaximumAlarms,
            MaximumSourceBytes: MaximumSourceBytes),
        ResourceKind: ForeSiteAdapter.WellResourceKind,
        Documentation: new("alarms.active-well", "Server-authorized read through the Integration Gateway.",
            """{"assetExternalId":"101"}""",
            """{"assetExternalId":"101","alarms":[{"id":"1001","typeId":1,"name":"High Rod Stress","startedAt":"2023-04-11T17:09:35.533Z","numericValue":117,"stringValue":null,"isDeviation":false,"isManuallySuppressed":null}],"retrievedAtUtc":"2026-11-15T12:00:00Z","source":"foresite:current-alarms"}""",
            "GET Alarms/CurrentAlarms/{wellId}."));

    public static bool IsValidRequest(ForeSiteActiveAlarmRequest request) => TryWellId(request.AssetExternalId, out _);

    public async Task<ForeSiteActiveAlarmResult> ReadAsync(ForeSiteActiveAlarmRequest request, CancellationToken cancellationToken)
    {
        if (!TryWellId(request.AssetExternalId, out var wellId))
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        using var response = await session.GetAsync("Alarms/CurrentAlarms/" + wellId.ToString(CultureInfo.InvariantCulture),
            cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new ForeSiteMeasurementException("FORESITE_ALARM_READ_FAILED");
        if (response.Content.Headers.ContentLength > MaximumSourceBytes)
            throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");

        try
        {
            await response.Content.LoadIntoBufferAsync(MaximumSourceBytes, cancellationToken);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) throw InvalidResponse();
            if (root.GetArrayLength() > MaximumAlarms)
                throw new ForeSiteMeasurementException("FORESITE_ALARM_ROW_LIMIT");

            var alarms = new List<ForeSiteActiveAlarm>(root.GetArrayLength());
            var ids = new HashSet<long>();
            foreach (var row in root.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateObject(row);
                if (ReadId(row, "WellId") != wellId)
                    throw new ForeSiteMeasurementException("FORESITE_ALARM_WELL_MISMATCH");
                var id = ReadId(row, "Id");
                if (!ids.Add(id) || !row.TryGetProperty("AlarmTypeId", out var type) ||
                    type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var typeId))
                    throw InvalidResponse();

                var time = OptionalText(row, "StartTime", 64);
                if (time is not { Length: >= 20 } ||
                    !(time.EndsWith('Z') || time.Length >= 25 && time[^6] is '+' or '-' && time[^3] == ':') ||
                    !row.GetProperty("StartTime").TryGetDateTimeOffset(out var started) || started == default)
                    throw InvalidResponse();

                string? name = null;
                bool? deviation = null;
                bool? suppressed = null;
                if (row.TryGetProperty("AlarmType", out var alarmType) && alarmType.ValueKind != JsonValueKind.Null)
                {
                    ValidateObject(alarmType);
                    if (alarmType.TryGetProperty("Id", out var nestedId) &&
                        (nestedId.ValueKind != JsonValueKind.Number || !nestedId.TryGetInt32(out var nestedTypeId) || nestedTypeId != typeId))
                        throw InvalidResponse();
                    name = OptionalText(alarmType, "AlarmType", 128);
                    deviation = OptionalFlag(alarmType, "IsDevationAlarm");
                    suppressed = OptionalFlag(alarmType, "IsAlarmManuallySuppressed");
                }

                decimal? numeric = null;
                if (row.TryGetProperty("NumericValue", out var number) && number.ValueKind != JsonValueKind.Null)
                {
                    if (number.ValueKind != JsonValueKind.Number || !number.TryGetDecimal(out var value)) throw InvalidResponse();
                    numeric = value;
                }
                alarms.Add(new(id.ToString(CultureInfo.InvariantCulture), typeId, name, started, numeric,
                    OptionalText(row, "StringValue", 256), deviation, suppressed));
            }
            return new(request.AssetExternalId, alarms, DateTimeOffset.UtcNow, "foresite:current-alarms");
        }
        catch (HttpRequestException) { throw new ForeSiteMeasurementException("FORESITE_ALARM_BODY_INVALID"); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static bool TryWellId(string? text, out long id)
    {
        id = 0;
        return text is { Length: > 0 and <= 20 } &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private static void ValidateObject(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in item.EnumerateObject())
            if (!fields.Add(field.Name)) throw InvalidResponse();
    }

    private static long ReadId(JsonElement item, string name) =>
        item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
        field.TryGetInt64(out var value) && value > 0 ? value : throw InvalidResponse();

    private static string? OptionalText(JsonElement item, string name, int limit)
    {
        if (!item.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString() is not { } text ||
            text.Length > limit || text.Any(char.IsControl)) throw InvalidResponse();
        return text;
    }

    private static bool? OptionalFlag(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null ? null : field.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidResponse()
        };

    private static ForeSiteMeasurementException InvalidResponse() => new("FORESITE_ALARM_RESPONSE_INVALID");
}
