using System.Reflection;
using System.Text.Json;
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
            PlatformComponentNames.Gateway,
            integrationGatewayUrl,
            cancellationToken);

        var worker = await ProbeAsync(
            PlatformComponentNames.DotNetRuntime,
            dotNetWorkerUrl,
            cancellationToken);

        return
        [
            new PlatformComponent(
                PlatformComponentNames.Server,
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

            string? sdk = null;
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is null or < 8192)
            {
                try
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("moduleSdkVersion", out var version) &&
                        version.ValueKind == JsonValueKind.String && version.GetString() is { Length: <= 64 } text) sdk = text;
                }
                catch (JsonException) { /* Older health endpoints return plain text. */ }
            }
            return new PlatformComponent(displayName, "installed", response.IsSuccessStatusCode
                ? PlatformComponentStatus.Healthy : PlatformComponentStatus.Degraded, sdk);
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
