using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using ICygNetClient = WellSiteAutoPilot.Integration.CygNet.Adapter.ICygNetClient;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// <see cref="ICygNetClient"/> over the CygNet API installed on the Gateway's server (see <see cref="CygNetRuntime"/>).
/// The connection names the owning facility service (for example UIS); the facility records live in the FAC service
/// associated with it. Current values come from the realtime client through <see cref="CygNetCurrentDataProvider"/>, with the
/// domain and the point map taken from the connection. A server without CygNet reports <see cref="CygNetNotInstalledException"/>.
/// </summary>
public sealed partial class CygNetApiClient : ICygNetClient
{
    /// <summary>Facilities read in one discovery. A larger service is reported as truncated, never as complete.</summary>
    private const int MaximumFacilities = 5000;
    private const int PageSize = 250;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    // The FAC service behind a connection's owning service does not change while the Gateway runs.
    private static readonly ConcurrentDictionary<string, object> FacilityClients = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var client = FacilityClient(connection);
            // Proves the facility service answers and the site.service pair has facilities, not just that FAC resolves.
            var tags = ((IEnumerable<dynamic>)client.GetFacilityTagList(FacilityQuery(connection), CancellationToken.None)).ToList();
            if (tags.Count == 0)
                throw new CygNetUnavailableException($"{connection.SiteService} has no facilities in domain {connection.Domain}. Check the site and service.");
            return (IReadOnlyList<string>)CygNetFreshness.Pick(
                tags.Select(tag => (string)tag.FacilityId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
        }, cancellationToken);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(connection, () =>
        {
            var runtime = CygNetRuntime.Require();
            var client = FacilityClient(connection);
            var wanted = ((IEnumerable<dynamic>)client.GetFacilityTagList(FacilityQuery(connection), CancellationToken.None))
                .Where(tag => connection.FacilityFilter is not { } filter || CygNetAdapter.Matches(filter, (string)tag.FacilityId))
                .Select(tag => (object)tag)
                .ToList();
            var truncated = wanted.Count > MaximumFacilities;
            var result = new List<CygNetFacility>();
            foreach (var page in wanted.Take(MaximumFacilities).Chunk(PageSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (dynamic record in client.ReadFacilityRecords((dynamic)runtime.FacilityTagList(page), CancellationToken.None))
                    result.Add(new CygNetFacility((string)record.Tag.FacilityId, (string?)record.Description, (string?)record.Type, (string?)record.Category));
            }
            return new CygNetFacilityList(result, truncated);
        }, cancellationToken);

    public async Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken)
    {
        var quantities = new Dictionary<string, CygNetQuantityMapping>(CygNetProviderOptions.DefaultQuantities, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mapping) in connection.PointMap ?? new Dictionary<string, CygNetPointMapping>())
            quantities[name] = new CygNetQuantityMapping(mapping.Udc, mapping.Unit);
        return await new CygNetCurrentDataProvider(new CygNetProviderOptions((ushort)connection.Domain, quantities))
            .GetCurrentAsync(connection.TagFor(facilityId), quantity, cancellationToken);
    }

    // Site and service are restricted to letters, digits, underscore and hyphen by the adapter's settings.
    private static string FacilityQuery(CygNetConnection connection) =>
        $"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'";

    private static dynamic FacilityClient(CygNetConnection connection)
    {
        var runtime = CygNetRuntime.Require();
        return FacilityClients.GetOrAdd($"{connection.Domain}/{connection.SiteService}", _ =>
            runtime.FacilityClient(runtime.AssociatedService(Service(runtime, connection), "FAC")));
    }

    private static object Service(CygNetRuntime runtime, CygNetConnection connection) =>
        runtime.Service((ushort)connection.Domain, connection.Site, connection.Service);

    // The CygNet API is synchronous and cannot be cancelled: run it off the caller's thread and bound the wait.
    // The gate keeps a call that never returns from piling up blocked threads: it holds its slot until it ends.
    private static async Task<T> Run<T>(CygNetConnection connection, Func<T> work, CancellationToken cancellationToken, TimeSpan? budget = null)
    {
        try
        {
            return await CygNetCallGate.Shared.RunAsync(
                $"{connection.Domain}/{connection.SiteService}", work, budget ?? Budget, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new CygNetConnectionException(connection.Domain);
        }
        catch (CygNetBusyException)
        {
            throw new CygNetConnectionException(connection.Domain);
        }
        catch (Exception error)
        {
            var cause = CygNetRuntime.Unwrap(error);
            // CygNet reports an unknown domain, an unknown site.service and an unreachable server alike: it cannot resolve the facility service.
            if (CygNetRuntime.IsMessagingFailure(cause))
                throw new CygNetConnectionException(connection.Domain, connection.SiteService, cause);
            if (cause is System.Net.Sockets.SocketException or System.IO.IOException)
                throw new CygNetConnectionException(connection.Domain);
            if (ReferenceEquals(cause, error))
                throw;
            ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
    }
}
