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
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

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

    // Site and service are restricted to letters, digits, underscore and hyphen by the adapter's settings.
    private static string FacilityQuery(CygNetConnection connection) =>
        $"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'";

    private static global::CygNet.API.Facilities.Client FacilityClient(CygNetConnection connection) =>
        FacilityClients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
            new global::CygNet.API.Facilities.Client(
                new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC)));

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
