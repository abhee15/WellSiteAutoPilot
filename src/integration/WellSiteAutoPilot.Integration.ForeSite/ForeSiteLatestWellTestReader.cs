using System.Globalization;
using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Reads the identity, source dates and provider status fields of a single reported test.</summary>
public sealed class ForeSiteLatestWellTestReader(ForeSiteSession session)
{
    internal const int MaximumSourceBytes = 1048576;

    public async Task<LatestWellTestResult> ReadAsync(LatestWellTestReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AssetExternalId is not { Length: > 0 and <= 20 } ||
            !long.TryParse(request.AssetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        using var response = await session.GetAsync("WellTests/GetLatestWellTestData/" + id.ToString(CultureInfo.InvariantCulture),
            cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_WELL_TEST_READ_FAILED");

        WellTestRecord? test = null;
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            if (response.Content.Headers.ContentLength > MaximumSourceBytes)
                throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");

            try { await response.Content.LoadIntoBufferAsync(MaximumSourceBytes, cancellationToken); }
            catch (HttpRequestException) { throw new ForeSiteMeasurementException("FORESITE_WELL_TEST_BODY_INVALID"); }

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
                test = ReadRecord(document.RootElement, id);
            }
            catch (JsonException) { throw InvalidResponse(); }
        }

        return new(request.AssetExternalId, test, DateTimeOffset.UtcNow, "foresite:well-tests");
    }

    private static WellTestRecord? ReadRecord(JsonElement root, long wellId)
    {
        if (root.ValueKind == JsonValueKind.Null) return null;
        if (root.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
            if (!fields.Add(property.Name)) throw InvalidResponse();

        if (!root.TryGetProperty("WellId", out var well) || well.ValueKind != JsonValueKind.Number ||
            !well.TryGetInt64(out var returnedWell) || returnedWell != wellId)
            throw new ForeSiteMeasurementException("FORESITE_WELL_TEST_IDENTITY_MISMATCH");

        if (!root.TryGetProperty("Id", out var record) || record.ValueKind != JsonValueKind.Number ||
            !record.TryGetInt64(out var testId) || testId <= 0)
            throw InvalidResponse();

        return new(testId.ToString(CultureInfo.InvariantCulture), ReadDate("SampleDate"), ReadDate("TestEndDate"),
            ReadField("Status", allowCode: true),
            new(ReadField("SPTCode", allowCode: true), ReadField("SPTCodeDescription"),
                ReadField("ValidationStatusCode", allowCode: true), ReadField("ValidationStatus")));

        DateTimeOffset? ReadDate(string name)
        {
            if (ReadField(name) is not { } text) return null;
            if ((text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date != default)
                return date;
            throw new ForeSiteMeasurementException("FORESITE_WELL_TEST_TIMESTAMP_INVALID");
        }

        string? ReadField(string name, bool allowCode = false)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number when allowCode && value.TryGetInt64(out _) => value.GetRawText(),
                _ => throw InvalidResponse()
            };
            if (text is { Length: > 512 } || text?.Any(char.IsControl) == true) throw InvalidResponse();
            return text;
        }
    }

    private static ForeSiteMeasurementException InvalidResponse() => new("FORESITE_WELL_TEST_RESPONSE_INVALID");
}
