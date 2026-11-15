using System.Collections.Concurrent;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

public sealed partial class CygNetApiClient : ICygNetPointCatalogClient
{
    private static readonly ConcurrentDictionary<string, object> PointConfigurationClients = new(StringComparer.OrdinalIgnoreCase);

    public async Task<CygNetPointList> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CygNetPointCatalog.IsFacilityId(facilityId) ||
            connection.TagFor(facilityId).Length > CygNetAdapter.MaximumFacilityTagLength ||
            connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facilityId))
            throw new CygNetReadException("CYGNET_FACILITY_INVALID", "The facility is outside this CygNet connection.");

        try
        {
            return await Run(connection, () =>
            {
                var runtime = CygNetRuntime.Require();
                dynamic client = PointConfigurationClients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
                    CygNetRuntime.PointConfigurationClient(runtime.AssociatedService(Service(runtime, connection), "PNT")));
                var escapedService = connection.SiteService.Replace("'", "''", StringComparison.Ordinal);
                var escapedFacility = facilityId.Replace("'", "''", StringComparison.Ordinal);
                var query = $"siteservice = '{escapedService}' AND facilityid = '{escapedFacility}'";
                var facilityTag = connection.TagFor(facilityId);
                var facility = runtime.FacilityTag(facilityTag);
                var points = new List<CygNetPointMetadata>();
                foreach (dynamic tag in client.GetPointTagList(query, CancellationToken.None))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (points.Count == CygNetPointCatalog.MaximumPoints)
                        return new CygNetPointList(facilityTag, points, Truncated: true);
                    var udc = (string)tag.UDC;
                    if (!CygNetPointCatalog.IsUdc(udc))
                        throw new CygNetReadException("CYGNET_CATALOG_RESPONSE_INVALID", "CygNet returned an invalid point code.");

                    // Use the authorized facility to construct the record key. A provider tag must never select another facility.
                    dynamic point = runtime.PointTag(facility, udc);
                    dynamic record = client.ReadPointRecord(point);
                    points.Add(new(udc, (string?)record.Description, (string?)record.PrimaryUnits));
                }
                return new CygNetPointList(facilityTag, points, Truncated: false);
            }, cancellationToken, TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException) { throw; }
        catch (CygNetUnavailableException) { throw; }
        catch (CygNetReadException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception error)
        {
            throw new CygNetReadException("CYGNET_CATALOG_READ_FAILED", "CygNet could not provide facility point metadata.", inner: error);
        }
    }
}
