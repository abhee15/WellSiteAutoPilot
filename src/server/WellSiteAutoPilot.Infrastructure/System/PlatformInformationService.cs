using System.Reflection;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Domain.System;

namespace WellSiteAutoPilot.Infrastructure.System;

public sealed class PlatformInformationService(
    HttpClient httpClient,
    string integrationGatewayUrl,
    string dotNetWorkerUrl) : IPlatformInformationService
{
    public async Task<IReadOnlyCollection<PlatformComponent>> GetComponentsAsync(
        CancellationToken cancellationToken = default)
    {
        var serverVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        var gateway = await ProbeAsync(
            "WellSite AutoPilot Integration Gateway",
            integrationGatewayUrl,
            cancellationToken);

        var worker = await ProbeAsync(
            "WellSite AutoPilot .NET Runtime",
            dotNetWorkerUrl,
            cancellationToken);

        return
        [
            new PlatformComponent(
                "WellSite AutoPilot Server",
                serverVersion,
                PlatformComponentStatus.Healthy),
            gateway,
            worker
        ];
    }

    private async Task<PlatformComponent> ProbeAsync(
        string displayName,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{baseUrl.TrimEnd('/')}/health/ready",
                cancellationToken);

            return new PlatformComponent(
                displayName,
                "installed",
                response.IsSuccessStatusCode
                    ? PlatformComponentStatus.Healthy
                    : PlatformComponentStatus.Degraded);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PlatformComponent(
                displayName,
                "installed",
                PlatformComponentStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new PlatformComponent(
                displayName,
                "installed",
                PlatformComponentStatus.Unavailable);
        }
    }
}
