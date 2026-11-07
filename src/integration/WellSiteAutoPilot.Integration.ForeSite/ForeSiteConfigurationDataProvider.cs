using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Explicit single-well details read; no discovery sweep and no provider DTO passthrough.</summary>
public sealed class ForeSiteConfigurationDataProvider(ForeSiteSession session) : IAssetConfigurationProvider
{
    public async ValueTask<AssetConfigurationResult> ReadConfigurationAsync(AssetConfigurationReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AssetExternalId is not { Length: > 0 and <= 20 } || !long.TryParse(request.AssetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw Failure("FORESITE_WELL_ID_INVALID");
        using var response = await session.GetAsync("Wells/Well/" + id.ToString(CultureInfo.InvariantCulture), cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? "FORESITE_CONFIGURATION_NOT_FOUND" : "FORESITE_CONFIGURATION_READ_FAILED");
        const int maximumSourceBytes = 1048576;
        if (response.Content.Headers.ContentLength > maximumSourceBytes) throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(bytes, cancellationToken)) != 0)
        {
            if (buffer.Length + count > maximumSourceBytes) throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
            await buffer.WriteAsync(bytes.AsMemory(0, count), cancellationToken);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Failure("FORESITE_CONFIGURATION_INVALID");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw Failure("FORESITE_CONFIGURATION_INVALID");
            if (!root.TryGetProperty("Id", out var identity) || identity.ValueKind != JsonValueKind.Number || !identity.TryGetInt64(out var returnedId) || returnedId != id)
                throw Failure("FORESITE_CONFIGURATION_IDENTITY_MISMATCH");
            var name = Text("Name");
            if (string.IsNullOrWhiteSpace(name)) throw Failure("FORESITE_CONFIGURATION_INVALID");
            DateTimeOffset? changed = null;
            if (Text("ChangeDate") is { } date)
            {
                if (!(date.EndsWith('Z') || date.Length >= 25 && date[^6] is '+' or '-' && date[^3] == ':') ||
                    !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp == default)
                    throw Failure("FORESITE_CONFIGURATION_TIMESTAMP_INVALID");
                changed = timestamp.ToUniversalTime();
            }
            var result = new AssetConfigurationResult(id.ToString(CultureInfo.InvariantCulture), name, ForeSiteAdapter.WellResourceKind,
                new(Text("WellType"), Text("FacilityId"), Text("Field"), Text("Lease"), Text("PrincipalDeviceType")),
                changed, DateTimeOffset.UtcNow, "foresite:well-details");
            if (JsonSerializer.SerializeToUtf8Bytes(result).Length > MeasurementPayloadLimits.MaximumReplyBytes)
                throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
            return result;

            string? Text(string field)
            {
                if (!root.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number when (field is "WellType" or "FacilityId" or "Field" or "Lease") && value.TryGetInt64(out _) => value.GetRawText(),
                    _ => throw Failure("FORESITE_CONFIGURATION_INVALID")
                };
                if (text is { Length: > 256 } || text?.Any(char.IsControl) == true) throw Failure("FORESITE_CONFIGURATION_INVALID");
                return text;
            }
        }
        catch (JsonException) { throw Failure("FORESITE_CONFIGURATION_INVALID"); }
    }
    private static ForeSiteMeasurementException Failure(string code) => new(code);
}
