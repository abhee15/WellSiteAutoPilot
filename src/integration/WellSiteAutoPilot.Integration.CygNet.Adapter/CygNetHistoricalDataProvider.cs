using System.Text.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Connection-bound historian adapter: explicit source scope, quantity allowlist and bounded output.</summary>
public sealed class CygNetHistoricalDataProvider(ICygNetClient client, CygNetConnection connection) : IBoundedHistoricalDataProvider
{
    public async ValueTask<HistoricalReadResult> ReadHistoryAsync(HistoricalReadRequest request, CancellationToken cancellationToken = default)
    {
        HistoricalReadLimits.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (client is not ICygNetHistoryClient history)
            throw Failure("CYGNET_HISTORY_UNSUPPORTED");
        var prefix = connection.SiteService + "::";
        if (!AdapterReadRules.CygNetQuantities.Contains(request.Quantity)) throw Failure("CYGNET_QUANTITY_UNSUPPORTED");
        if (!request.AssetExternalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || request.AssetExternalId.Length == prefix.Length ||
            request.AssetExternalId.IndexOf("::", prefix.Length, StringComparison.Ordinal) >= 0)
            throw Failure("CYGNET_FACILITY_INVALID");
        var facility = request.AssetExternalId[prefix.Length..];
        if (connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facility))
            throw Failure("CYGNET_FACILITY_INVALID");
        try
        {
            var result = await history.ReadHistoryAsync(connection, facility, request, cancellationToken);
            if (result.Quantity != request.Quantity || result.Aggregation != "Raw" || string.IsNullOrWhiteSpace(result.Source) ||
                result.StartUtc != request.StartUtc || result.EndUtc != request.EndUtc || result.Samples.Count > request.MaximumSamples ||
                result.Samples.Any(sample => sample.Quantity != request.Quantity || sample.TimestampUtc < request.StartUtc ||
                    sample.TimestampUtc >= request.EndUtc || !MeasurementPayloadLimits.IsValid(sample) ||
                    string.IsNullOrWhiteSpace(sample.Quality) || string.IsNullOrWhiteSpace(sample.Source)) ||
                !result.Samples.Select(sample => sample.TimestampUtc).SequenceEqual(result.Samples.OrderBy(sample => sample.TimestampUtc).Select(sample => sample.TimestampUtc)))
                throw Failure("CYGNET_HISTORY_INVALID");
            if (JsonSerializer.SerializeToUtf8Bytes(result).Length > MeasurementPayloadLimits.MaximumReplyBytes)
                throw Failure("CYGNET_PAYLOAD_TOO_LARGE");
            return result;
        }
        catch (CygNetReadException error) { throw Failure(error.Code); }
        catch (CygNetUnavailableException) { throw Failure("CYGNET_UNAVAILABLE"); }
    }

    public async ValueTask<IReadOnlyCollection<EngineeringValue>> GetHistoryAsync(string assetExternalId, string quantity,
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default)
    {
        var result = await ReadHistoryAsync(new(assetExternalId, quantity, startUtc, endUtc), cancellationToken);
        if (result.Truncated) throw Failure("CYGNET_HISTORY_TRUNCATED");
        return result.Samples;
    }

    private static WellSiteAutoPilotException Failure(string code) => new(code, code is "CYGNET_FACILITY_INVALID" or "CYGNET_QUANTITY_UNSUPPORTED" ? FailureKind.Validation : FailureKind.DependencyUnavailable,
        "The selected CygNet connection could not provide the requested bounded history.");
}
