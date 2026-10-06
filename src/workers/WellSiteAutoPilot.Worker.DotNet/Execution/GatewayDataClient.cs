using System.Net.Http.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class GatewayDataClient(HttpClient httpClient)
{
    public async Task<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);

        var url =
            $"/api/internal/v1/data/current?assetExternalId={Uri.EscapeDataString(assetExternalId)}" +
            $"&quantity={Uri.EscapeDataString(quantity)}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EngineeringValue>(
            cancellationToken: cancellationToken) ??
            throw new InvalidOperationException(
                "The Integration Gateway returned an empty engineering value.");
    }
}
