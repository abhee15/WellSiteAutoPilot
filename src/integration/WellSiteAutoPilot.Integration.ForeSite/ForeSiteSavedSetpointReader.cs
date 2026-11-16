using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public sealed record ForeSiteSavedSetpointRequest([property: JsonRequired] string AssetExternalId);
public sealed record ForeSiteSavedSetpoint(string ElementId, string? Description, string? DisplayValue, string? OriginalValue,
    string? Unit, string? OriginalUnit, decimal? RangeLow, decimal? RangeHigh, bool? IsReadOnly);
public sealed record ForeSiteSavedSetpointGroup(string Type, string? Name, int? Ordinal, string? DeviceType,
    DateTimeOffset? TransactionTime, bool? IsWritable, IReadOnlyList<ForeSiteSavedSetpoint> Setpoints);
public sealed record ForeSiteSavedSetpointResult(string AssetExternalId, IReadOnlyList<ForeSiteSavedSetpointGroup> Groups,
    DateTimeOffset RetrievedAtUtc, string Source);

public sealed class ForeSiteSavedSetpointReader(ForeSiteSession session)
{
    private const int MaximumGroups = 32;
    private const int MaximumElements = 128;
    private const int MaximumSourceBytes = 1048576;

    public static readonly AdapterOperationDescriptor Operation = new(
        "configuration.saved-setpoints", "Saved setpoints", "Inspect ForeSite's saved setpoint groups without requesting a controller scan.",
        ProviderCapabilityKind.ExtensionRead, "foresite.saved-setpoints", "1.0",
        new("foresite.saved-setpoints-request/v1", [new("assetExternalId", "string", true, "Imported numeric ForeSite well ID.")]),
        new("foresite.saved-setpoints-result/v1", [
            new("assetExternalId", "string", true, "Exact requested well ID."),
            new("groups", "object[]", true, "Saved provider groups and visible elements in provider order."),
            new("groups[].type", "string", true, "Provider data group type."),
            new("groups[].name", "string|null", false, "Provider data group label."),
            new("groups[].ordinal", "integer|null", false, "Provider data group ordinal."),
            new("groups[].deviceType", "string|null", false, "Provider controller type."),
            new("groups[].isWritable", "boolean|null", false, "Provider group flag, for inspection only."),
            new("groups[].transactionTime", "date-time|null", false, "Source group transaction timestamp with its offset."),
            new("groups[].setpoints", "object[]", true, "Visible elements with their provider metadata."),
            new("groups[].setpoints[].elementId", "string", true, "Provider element code within the group."),
            new("groups[].setpoints[].description", "string|null", false, "Provider element description."),
            new("groups[].setpoints[].displayValue", "string|null", false, "Value as displayed by ForeSite."),
            new("groups[].setpoints[].originalValue", "string|null", false, "Original saved value from ForeSite."),
            new("groups[].setpoints[].unit", "string|null", false, "Displayed unit from ForeSite."),
            new("groups[].setpoints[].originalUnit", "string|null", false, "Original unit from ForeSite."),
            new("groups[].setpoints[].rangeLow", "decimal|null", false, "Provider range lower bound."),
            new("groups[].setpoints[].rangeHigh", "decimal|null", false, "Provider range upper bound."),
            new("groups[].setpoints[].isReadOnly", "boolean|null", false, "Provider element flag, for inspection only."),
            new("retrievedAtUtc", "date-time", true, "Separate retrieval instant."),
            new("source", "string", true, "ForeSite saved setpoint provenance.")]),
        "Group transaction time is independent of retrieval time. A saved value may be old.",
        "Units, ranges and read-only flags are provider metadata. No unit conversion or write authority is inferred.",
        new(1, 4096, MeasurementPayloadLimits.MaximumReplyBytes, TimeoutSeconds: 6, MaximumSamples: MaximumElements,
            MaximumSourceBytes: MaximumSourceBytes),
        ResourceKind: ForeSiteAdapter.WellResourceKind,
        Documentation: new("configuration.saved-setpoints", "Server-authorized read through the Integration Gateway.",
            """{"assetExternalId":"101"}""",
            """{"assetExternalId":"101","groups":[{"type":"SetPoints","name":"Basic SetPoints Configuration","ordinal":null,"deviceType":"WellPilotRPOC","transactionTime":"2022-09-29T16:31:27.863Z","isWritable":true,"setpoints":[{"elementId":"IdleTime","description":"Idle Time Following Pump Off","displayValue":"00:01:00","originalValue":"60","unit":"s","originalUnit":"s","rangeLow":0,"rangeHigh":4294967295,"isReadOnly":false}]}],"retrievedAtUtc":"2026-11-15T12:00:00Z","source":"foresite:saved-setpoints"}""",
            "GET Surveillance/Well/AllSavedSetPointParams?WellId={wellId}&scanOption=0."));

    public static bool IsValidRequest(ForeSiteSavedSetpointRequest request) => TryWellId(request.AssetExternalId, out _);

    public async Task<ForeSiteSavedSetpointResult> ReadAsync(ForeSiteSavedSetpointRequest request, CancellationToken cancellationToken)
    {
        if (!TryWellId(request.AssetExternalId, out var wellId))
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        using var response = await session.GetAsync(
            $"Surveillance/Well/AllSavedSetPointParams?WellId={wellId.ToString(CultureInfo.InvariantCulture)}&scanOption=0",
            cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new ForeSiteMeasurementException("FORESITE_SETPOINT_READ_FAILED");
        if (response.Content.Headers.ContentLength > MaximumSourceBytes)
            throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");

        try
        {
            await response.Content.LoadIntoBufferAsync(MaximumSourceBytes, cancellationToken);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            return Project(document.RootElement, request.AssetExternalId, wellId, cancellationToken);
        }
        catch (HttpRequestException) { throw new ForeSiteMeasurementException("FORESITE_SETPOINT_BODY_INVALID"); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static ForeSiteSavedSetpointResult Project(JsonElement root, string externalId, long wellId,
        CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        if (root.GetArrayLength() > MaximumGroups) throw RowLimit();

        var groups = new List<ForeSiteSavedSetpointGroup>(root.GetArrayLength());
        var inspected = 0;
        foreach (var group in root.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateObject(group);
            if (!group.TryGetProperty("WellId", out var identity) || identity.ValueKind != JsonValueKind.Number ||
                !identity.TryGetInt64(out var returnedWell) || returnedWell != wellId)
                throw new ForeSiteMeasurementException("FORESITE_SETPOINT_WELL_MISMATCH");
            if (!string.Equals(Text(group, "ErrorMessage", 128), "success", StringComparison.OrdinalIgnoreCase))
                throw new ForeSiteMeasurementException("FORESITE_SETPOINT_GROUP_FAILED");
            if (!group.TryGetProperty("SetPointsList", out var elements) || elements.ValueKind != JsonValueKind.Array)
                throw InvalidResponse();

            inspected += elements.GetArrayLength();
            if (inspected > MaximumElements) throw RowLimit();

            var visible = new List<ForeSiteSavedSetpoint>();
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in elements.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateObject(item);
                var hidden = Flag(item, "IsHidden") ?? throw InvalidResponse();
                if (hidden) continue;

                var id = RequiredText(item, "ElementId", 128);
                if (!identities.Add(id)) throw InvalidResponse();
                visible.Add(new(id, Text(item, "Description", 256), Text(item, "Value", 256), Text(item, "OriginalValue", 256),
                    Text(item, "UnitKey", 32), Text(item, "OriginalUnitKey", 32), Range(item, "RangeLow"), Range(item, "RangeHigh"),
                    Flag(item, "IsReadOnly")));
            }

            int? ordinal = null;
            if (group.TryGetProperty("DataGroupOrdinal", out var rawOrdinal) && rawOrdinal.ValueKind != JsonValueKind.Null)
            {
                if (rawOrdinal.ValueKind != JsonValueKind.Number || !rawOrdinal.TryGetInt32(out var ordinalValue)) throw InvalidResponse();
                ordinal = ordinalValue;
            }
            groups.Add(new(RequiredText(group, "DataGroupType", 128), Text(group, "DataGroupName", 256), ordinal,
                Text(group, "DeviceType", 128), TransactionTime(group), Flag(group, "IsWritable"), visible));
        }
        return new(externalId, groups, DateTimeOffset.UtcNow, "foresite:saved-setpoints");
    }

    private static bool TryWellId(string? text, out long id)
    {
        id = 0;
        return text is { Length: > 0 and <= 20 } &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in value.EnumerateObject())
            if (!fields.Add(field.Name)) throw InvalidResponse();
    }

    private static string RequiredText(JsonElement item, string name, int limit) =>
        Text(item, name, limit) is { } text && !string.IsNullOrWhiteSpace(text) ? text : throw InvalidResponse();

    private static string? Text(JsonElement item, string name, int limit)
    {
        if (!item.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString() is not { } text ||
            text.Length > limit || text.Any(char.IsControl)) throw InvalidResponse();
        return text;
    }

    private static bool? Flag(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null ? null : field.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidResponse()
        };

    private static decimal? Range(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        return field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var value) ? value : throw InvalidResponse();
    }

    private static DateTimeOffset? TransactionTime(JsonElement group)
    {
        var text = Text(group, "TransactionTimeStamp", 64);
        if (text is null) return null;
        if (text.Length < 20 || !(text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') ||
            !group.GetProperty("TransactionTimeStamp").TryGetDateTimeOffset(out var timestamp) || timestamp == default)
            throw InvalidResponse();
        return timestamp;
    }

    private static ForeSiteMeasurementException RowLimit() => new("FORESITE_SETPOINT_ROW_LIMIT");
    private static ForeSiteMeasurementException InvalidResponse() => new("FORESITE_SETPOINT_RESPONSE_INVALID");
}
