using CygNet.API.Core;
using CygNet.Data.Core;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// CygNet-backed facility reader (POC). The connection names the owning facility service (for example UIS);
/// the facility records live in the FAC service associated with it, found through ServiceInformation.
/// </summary>
public sealed class CygNetFacilityReader : ICygNetFacilityReader
{
    private const int MaximumFacilities = 500;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    static CygNetFacilityReader() => CygNetAssemblyResolver.Register();

    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Run(() => { _ = new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC); }, cancellationToken);

    public Task<IReadOnlyCollection<CygNetFacilitySnapshot>> ReadFacilitiesAsync(
        CygNetConnection connection, CancellationToken cancellationToken) =>
        Run<IReadOnlyCollection<CygNetFacilitySnapshot>>(() =>
        {
            var fac = new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC);
            var client = new global::CygNet.API.Facilities.Client(fac);
            var tags = client.GetFacilityTagList(
                $"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'",
                CancellationToken.None);
            var result = new List<CygNetFacilitySnapshot>();
            foreach (var tag in tags.Take(MaximumFacilities))
            {
                var record = client.ReadFacility(tag);
                result.Add(new CygNetFacilitySnapshot(tag.FacilityId, record.Description, record.Type));
            }
            return result;
        }, cancellationToken);

    private static DomainSiteService Service(CygNetConnection connection) =>
        new((ushort)connection.Domain, connection.Site, connection.Service);

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CygNetUnavailableException($"CygNet could not be reached: {ex.Message}");
        }
    }

    private static Task<object?> Run(Action work, CancellationToken cancellationToken) =>
        Run<object?>(() => { work(); return null; }, cancellationToken);
}
