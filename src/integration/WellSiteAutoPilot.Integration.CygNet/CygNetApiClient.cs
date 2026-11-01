using CygNet.API.Core;
using CygNet.Data.Core;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

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
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    static CygNetApiClient() => CygNetAssemblyResolver.Register();

    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(() => { _ = new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC); return 0; }, cancellationToken);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(() =>
        {
            var fac = new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC);
            var client = new global::CygNet.API.Facilities.Client(fac);
            // Site and service are restricted to letters, digits, underscore and hyphen by the adapter's settings.
            var tags = client.GetFacilityTagList(
                $"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'", CancellationToken.None);
            var result = new List<CygNetFacility>();
            var truncated = false;
            foreach (var tag in tags)
            {
                if (result.Count >= MaximumFacilities) { truncated = true; break; }
                if (connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, tag.FacilityId)) continue;
                var record = client.ReadFacility(tag);
                result.Add(new CygNetFacility(tag.FacilityId, record.Description, record.Type));
            }
            return new CygNetFacilityList(result, truncated);
        }, cancellationToken);

    public async Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) =>
        await new CygNetCurrentDataProvider(options with { Domain = (ushort)connection.Domain })
            .GetCurrentAsync(connection.TagFor(facilityId), quantity, cancellationToken);

    private static DomainSiteService Service(CygNetConnection connection) =>
        new((ushort)connection.Domain, connection.Site, connection.Service);

    // The CygNet API is synchronous and cannot be cancelled: run it off the caller's thread and bound the wait.
    private static async Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(work, CancellationToken.None).WaitAsync(Budget, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new CygNetUnavailableException("CygNet did not answer in time.");
        }
    }
}
