using System.Net.Http.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

public sealed class SimulatorCurrentDataProvider(
    HttpClient httpClient) :
    ICurrentDataProvider,
    IProviderHealthProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);

        var asset = Uri.EscapeDataString(assetExternalId);
        var requestedQuantity = Uri.EscapeDataString(quantity);

        using var response = await httpClient.GetAsync(
            $"/api/sim/v1/assets/{asset}/current/{requestedQuantity}",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EngineeringValue>(
            cancellationToken: cancellationToken) ??
            throw new InvalidOperationException(
                "The provider simulator returned an empty engineering value.");
    }

    public async ValueTask<ProviderHealth> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                "/health/ready",
                cancellationToken);

            return new ProviderHealth(
                "simulator",
                "Provider Simulator",
                response.IsSuccessStatusCode
                    ? ProviderHealthState.Healthy
                    : ProviderHealthState.Degraded,
                response.IsSuccessStatusCode
                    ? null
                    : $"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return new ProviderHealth(
                "simulator",
                "Provider Simulator",
                ProviderHealthState.Unavailable,
                exception.GetType().Name);
        }
    }
}
