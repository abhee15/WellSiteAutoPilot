using CygNet.API.Core;
using CygNet.Data.Core;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

public sealed partial class CygNetApiClient
{
    // Deliberately does NOT implement ICygNetHistoryClient yet. The host probe can test this method,
    // while Gateway metadata and execution keep live history unsupported until native validation passes.
    private static class Historians
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, global::CygNet.API.Historian.Client> Clients =
            new(StringComparer.OrdinalIgnoreCase);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance method kept so the client can implement ICygNetHistoryClient once history is validated live.")]
    public async Task<HistoricalReadResult> ReadHistoryAsync(CygNetConnection connection, string facilityId,
        HistoricalReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        HistoricalReadLimits.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AdapterReadRules.CygNetQuantities.Contains(request.Quantity) ||
            !string.Equals(connection.TagFor(facilityId), request.AssetExternalId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(facilityId) || facilityId.Any(character => character is ':' or '/' or '\\' or '*' or '?' || char.IsControl(character)) ||
            connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facilityId))
            throw new CygNetReadException("CYGNET_HISTORY_SCOPE_INVALID", "The history request does not match this connection and facility.");
        var mapping = connection.PointMap?.TryGetValue(request.Quantity, out var local) == true
            ? local : CygNetProviderOptions.DefaultQuantities.TryGetValue(request.Quantity, out var configured)
                ? new CygNetPointMapping(configured.Udc, configured.Unit) : null;
        if (mapping is null || mapping.Udc.Length is 0 or > 32 ||
            mapping.Udc.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new CygNetReadException("CYGNET_QUANTITY_UNSUPPORTED", "The requested quantity has no valid point mapping.");
        try
        {
            return await Run(connection, () =>
            {
                var historian = Historians.Clients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
                    new global::CygNet.API.Historian.Client(new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.VHS)));
                var name = new global::CygNet.Data.Historian.Name { ID = $"{connection.SiteService}:{facilityId}_{mapping.Udc}" };
                IEnumerable<CygNetHistoryEntry> Entries()
                {
                    foreach (var entry in historian.GetHistoricalEntriesReverse(name, request.StartUtc.UtcDateTime, request.EndUtc.UtcDateTime, false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var kind = entry.ValueType switch
                        {
                            global::CygNet.Data.Historian.HistoricalEntryValueType.Double => CygNetHistoryValueKind.FloatingPoint,
                            global::CygNet.Data.Historian.HistoricalEntryValueType.Int64 => CygNetHistoryValueKind.SignedInteger,
                            global::CygNet.Data.Historian.HistoricalEntryValueType.UTF8 => CygNetHistoryValueKind.Text,
                            _ => throw new CygNetReadException("CYGNET_HISTORY_TYPE_UNSUPPORTED", "The historian entry is not a supported numeric type.")
                        };
                        yield return new(entry.Timestamp, kind, entry.Value, entry.BaseStatus.ToString());
                    }
                }
                return CygNetHistoryValues.ProjectReverse(request, mapping.Unit, mapping.Udc, Entries(), cancellationToken: cancellationToken);
            }, cancellationToken, TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException) { throw; }
        catch (CygNetReadException) { throw; }
        catch (CygNetUnavailableException) { throw; }
        catch (UnauthorizedAccessException error)
        {
            throw new CygNetReadException("CYGNET_HISTORY_FORBIDDEN", "The Windows account cannot read this history.", inner: error);
        }
        catch (Exception error)
        {
            throw new CygNetReadException("CYGNET_HISTORY_READ_FAILED", "CygNet could not provide the requested history.", inner: error);
        }
    }
}
