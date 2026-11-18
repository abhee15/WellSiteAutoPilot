using System.Net;
using System.Net.Http.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>
/// A CygNet stand-in for development and CI, where no CygNet install or licence exists. It answers the same
/// <see cref="ICygNetClient"/> seam as the real client from the provider simulator, so everything above it (settings,
/// discovery, read rules, quality handling) runs unchanged. Current values only, like the real adapter today.
/// It exists only when <c>CygNet:Simulator:BaseUrl</c> is set; a deployment without that setting never uses it.
/// </summary>
public sealed class SimulatedCygNetClient(HttpClient http) : ICygNetClient
{
    public async Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        (await FacilitiesAsync(cancellationToken)).Select(item => item.Id).Take(3).ToArray();

    public async Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        new(await FacilitiesAsync(cancellationToken));

    public async Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken)
    {
        using var response = await Send(
            $"/cygnet/sim/facilities/{Uri.EscapeDataString(facilityId)}/current/{Uri.EscapeDataString(quantity)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new CygNetReadException("CYGNET_POINT_NOT_FOUND", "CygNet has no such point for this facility.");
        if (!response.IsSuccessStatusCode) throw Unavailable();
        var value = await response.Content.ReadFromJsonAsync<EngineeringValue>(cancellationToken) ?? throw Unavailable();
        return value with { Source = "cygnet:simulator:" + facilityId, RetrievedAtUtc = DateTimeOffset.UtcNow };
    }

    private async Task<IReadOnlyList<CygNetFacility>> FacilitiesAsync(CancellationToken cancellationToken)
    {
        using var response = await Send("/cygnet/sim/facilities", cancellationToken);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        var ids = await response.Content.ReadFromJsonAsync<string[]>(cancellationToken) ?? throw Unavailable();
        return ids.Select(id => new CygNetFacility(id, "Simulated " + id, "RRL", "Rod pump")).ToArray();
    }

    private async Task<HttpResponseMessage> Send(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(path, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
    }

    private static CygNetUnavailableException Unavailable() => new("The simulated CygNet is not available.");
}
