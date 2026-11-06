using System.Collections.Concurrent;
using CygNet.API.Core;
using CygNet.Data.Core;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using ICygNetClient = WellSiteAutoPilot.Integration.CygNet.Adapter.ICygNetClient;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// CygNet.API-backed <see cref="ICygNetClient"/> for the opt-in build. The connection names the owning facility service
/// (for example UIS); the facility records live in the FAC service associated with it. Current values come from the
/// realtime client through <see cref="CygNetCurrentDataProvider"/>, with the domain taken from the connection and the
/// quantity-to-point map from the Gateway's configuration.
/// </summary>
/// <remarks>Compiled only where the CygNet libraries exist; ported from a proof of concept that ran against a live domain.</remarks>
public sealed class CygNetApiClient(CygNetProviderOptions options) : ICygNetClient
{
    /// <summary>Facilities read in one discovery. A larger service is reported as truncated, never as complete.</summary>
    private const int MaximumFacilities = 5000;
    private const int PageSize = 250;
    /// <summary>A facility has a few hundred points; the cap stops a misconfigured service from running a long read.</summary>
    private const int MaximumPoints = 1000;
    /// <summary>A month at one entry a minute would be 44,640; the cap keeps a dense point from holding the call.</summary>
    private const int MaximumHistoryEntries = 50_000;
    // Held in a nested class so the Historian assembly loads only after the assembly resolver is registered.
    private static class Historians
    {
        public static readonly ConcurrentDictionary<string, global::CygNet.API.Historian.Client> Clients = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<string, global::CygNet.API.Points.ConfigClient> PointClients =
        new(StringComparer.OrdinalIgnoreCase);

    // The FAC service behind a connection's owning service does not change while the Gateway runs.
    private static readonly ConcurrentDictionary<string, global::CygNet.API.Facilities.Client> FacilityClients =
        new(StringComparer.OrdinalIgnoreCase);

    static CygNetApiClient() => CygNetAssemblyResolver.Register();

    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var client = FacilityClient(connection);
            // Proves the facility service answers and the site.service pair has facilities, not just that FAC resolves.
            var tags = client.GetFacilityTagList(FacilityQuery(connection), CancellationToken.None);
            if (tags.Count == 0)
                throw new CygNetUnavailableException($"{connection.SiteService} has no facilities in domain {connection.Domain}. Check the site and service.");
            return (IReadOnlyList<string>)CygNetFreshness.Pick(tags.Select(tag => tag.FacilityId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
        }, cancellationToken);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var client = FacilityClient(connection);
            var wanted = client.GetFacilityTagList(FacilityQuery(connection), CancellationToken.None)
                .Where(tag => connection.FacilityFilter is not { } filter || CygNetAdapter.Matches(filter, tag.FacilityId))
                .ToList();
            var truncated = wanted.Count > MaximumFacilities;
            var result = new List<CygNetFacility>();
            foreach (var page in wanted.Take(MaximumFacilities).Chunk(PageSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var record in client.ReadFacilityRecords(page, CancellationToken.None))
                    result.Add(new CygNetFacility(record.Tag.FacilityId, record.Description, record.Type, record.Category));
            }
            return new CygNetFacilityList(result, truncated);
        }, cancellationToken);

    public async Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken)
    {
        var quantities = new Dictionary<string, CygNetQuantityMapping>(options.Quantities, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mapping) in connection.PointMap ?? new Dictionary<string, CygNetPointMapping>())
            quantities[name] = new CygNetQuantityMapping(mapping.Udc, mapping.Unit);
        return await new CygNetCurrentDataProvider(new CygNetProviderOptions((ushort)connection.Domain, quantities))
            .GetCurrentAsync(connection.TagFor(facilityId), quantity, cancellationToken);
    }

    public Task<IReadOnlyList<CygNetPoint>> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var client = PointsClient(connection);
            var site = connection.SiteService;
            var tags = client.GetPointTagList($"siteservice = '{site}' AND facilityid = '{facilityId.Replace("'", "''")}'", CancellationToken.None);
            if (tags.Count == 0) throw new CygNetReadException("CYGNET_FACILITY_NOT_FOUND", "CygNet has no points for this facility.");
            var points = new List<CygNetPoint>();
            foreach (var tag in tags.Take(MaximumPoints))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = client.ReadPointRecord(tag);
                points.Add(new CygNetPoint(tag.UDC, record.Description ?? "", record.PrimaryUnits ?? ""));
            }
            return (IReadOnlyList<CygNetPoint>)points;
        }, cancellationToken);

    public Task<CygNetHistory> ReadHistoryAsync(
        CygNetConnection connection, string facilityId, string udc, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var client = HistorianClient(connection);
            // The history long id is SITE.SERVICE:FACILITY_UDC.
            var name = new global::CygNet.Data.Historian.Name { ID = $"{connection.SiteService}:{facilityId}_{udc}" };
            var entries = new List<CygNetHistoryEntry>();
            var truncated = false;
            foreach (var entry in client.GetHistoricalEntriesReverse(name, startUtc.UtcDateTime, endUtc.UtcDateTime, false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= MaximumHistoryEntries) { truncated = true; break; }
                double value;
                switch (entry.ValueType)
                {
                    case global::CygNet.Data.Historian.HistoricalEntryValueType.Double: value = BitConverter.ToDouble(entry.Value, 0); break;
                    case global::CygNet.Data.Historian.HistoricalEntryValueType.Int64: value = BitConverter.ToInt64(entry.Value, 0); break;
                    case global::CygNet.Data.Historian.HistoricalEntryValueType.UTF8
                        when double.TryParse(System.Text.Encoding.UTF8.GetString(entry.Value), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out value): break;
                    default: continue;
                }
                var stamp = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc);
                entries.Add(new CygNetHistoryEntry(stamp, value, entry.BaseStatus.HasFlag(global::CygNet.Data.Core.BaseStatusFlags.Unreliable)));
            }
            entries.Reverse();
            return new CygNetHistory(entries, truncated);
        }, cancellationToken);

    private static global::CygNet.API.Historian.Client HistorianClient(CygNetConnection connection) =>
        Historians.Clients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
            new global::CygNet.API.Historian.Client(
                new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.VHS)));

    // Site and service are restricted to letters, digits, underscore and hyphen by the adapter's settings.
    private static string FacilityQuery(CygNetConnection connection) =>
        $"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'";

    private static global::CygNet.API.Facilities.Client FacilityClient(CygNetConnection connection) =>
        FacilityClients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
            new global::CygNet.API.Facilities.Client(
                new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC)));

    private static global::CygNet.API.Points.ConfigClient PointsClient(CygNetConnection connection) =>
        PointClients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
            new global::CygNet.API.Points.ConfigClient(
                new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.PNT)));

    private static DomainSiteService Service(CygNetConnection connection) =>
        new((ushort)connection.Domain, connection.Site, connection.Service);

    // The CygNet API is synchronous and cannot be cancelled: run it off the caller's thread and bound the wait.
    // The gate keeps a call that never returns from piling up blocked threads: it holds its slot until it ends.
    private static async Task<T> Run<T>(CygNetConnection connection, Func<T> work, CancellationToken cancellationToken)
    {
        try
        {
            return await CygNetCallGate.Shared.RunAsync(
                $"{connection.Domain}/{connection.SiteService}", work, Budget, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new CygNetUnavailableException("CygNet did not answer in time.");
        }
        catch (CygNetBusyException)
        {
            throw new CygNetUnavailableException("CygNet is not answering and earlier calls are still waiting. Try again shortly.");
        }
    }
}
