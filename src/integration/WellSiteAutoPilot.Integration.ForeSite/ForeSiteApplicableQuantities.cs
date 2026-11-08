using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// Native WellQuantity enum IDs returned for one ForeSite well. These IDs are NOT module-facing quantity keys.
/// The provider enum mapping must be independently verified before using an ID to authorize a named input.
/// </summary>
public sealed record ForeSiteApplicableQuantityIds(string AssetExternalId, IReadOnlySet<int> EnumIds)
{
    public bool ContainsNativeId(int id) => EnumIds.Contains(id);
}

/// <summary>
/// Read-only, bounded provider-native applicability discovery. Uses the same authenticated ForeSiteSession
/// as data acquisition. No inference from populated status fields and no unverified ID-to-name mapping.
/// </summary>
public static class ForeSiteApplicableQuantities
{
    public const int MaximumSourceBytes = 16 * 1024;
    public const int MaximumQuantityIds = 256;

    public static async Task<ForeSiteApplicableQuantityIds> ReadAsync(
        ForeSiteSession session, string externalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        using var response = await session.GetAsync(
            "Surveillance/AvailableQuantities/" + wellId.ToString(CultureInfo.InvariantCulture),
            cancellationToken, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));

        if (response.Content.Headers.ContentLength is > MaximumSourceBytes)
            throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_TOO_LARGE");

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await source.ReadAsync(chunk.AsMemory(), cancellationToken)) != 0)
            {
                if (buffer.Length + read > MaximumSourceBytes)
                    throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_TOO_LARGE");
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            buffer.Position = 0;
            using var document = await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 4 },
                cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_RESPONSE_INVALID");

            var identifiers = new HashSet<int>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (identifiers.Count >= MaximumQuantityIds || item.ValueKind != JsonValueKind.Number ||
                    !item.TryGetInt32(out var id) || id <= 0 || id > ushort.MaxValue || !identifiers.Add(id))
                    throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_RESPONSE_INVALID");
            }

            return new(externalId, identifiers.ToFrozenSet());
        }
        catch (JsonException)
        {
            throw new ForeSiteMeasurementException("FORESITE_APPLICABILITY_RESPONSE_INVALID");
        }
    }
}
