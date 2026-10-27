using System.Net;
using WellSiteAutoPilot.Application.Executions;
using System.Net.Http.Headers;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Server.Connections;

/// <summary>
/// Asks the Integration Gateway about adapters, presenting the Server's own caller key. The key is read when a
/// request is made, so a Server without one still starts and only connection features report the gap.
/// </summary>
public sealed class GatewayAdapterClient(HttpClient client, IConfiguration configuration) : IGatewayAdapters, IExecutionMeasurementGateway
{
    private const string Base = "api/internal/v1/adapters";

    public async Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, Base);
        using var response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdapterDescriptor[]>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(adapterKey)}/validate", settings);
        using var response = await SendAsync(request, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return (await response.Content.ReadFromJsonAsync<AdapterValidationResponse>(cancellationToken))?.Problems ?? [];
    }

    public async Task<AdapterTestResult?> TestAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(adapterKey)}/test", settings);
        using var response = await SendAsync(request, cancellationToken, allowNotFound: true);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<AdapterTestResult>(cancellationToken);
    }

    public async Task<DiscoveryResult?> DiscoverAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(adapterKey)}/discover", settings);
        using var response = await SendAsync(request, cancellationToken, allowNotFound: true);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<DiscoveryResult>(cancellationToken);
    }

    public async Task<EngineeringValue> GetCurrentAsync(AuthorizedExecutionRead read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var request = Authorized(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(read.AdapterKey)}/current");
        request.Content = JsonContent.Create(new ConnectionMeasurementRequest(read.ConnectionId, read.ConnectionRevision,
            read.Settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), read.ExternalId, read.Quantity));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Preserve bounded typed errors, never upstream detail or connection settings in the worker reply.
            var problem = await response.Content.ReadFromJsonAsync<WellSiteAutoPilot.Http.WellSiteProblemDetails>(cancellationToken);
            var code = problem?.Code;
            throw new WellSiteAutoPilotException(code is { Length: > 0 and <= 128 } &&
                code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? code : "EXECUTION_READ_GATEWAY_FAILED",
                FailureKind.DependencyUnavailable, "The authorized measurement could not be acquired.");
        }
        return await response.Content.ReadFromJsonAsync<EngineeringValue>(cancellationToken)
            ?? throw Unavailable("The integration gateway returned no measurement.");
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var key = configuration[ServiceCallerClient.KeyConfigurationName];
        if (string.IsNullOrWhiteSpace(key))
            throw Unavailable("The server has no key for the integration gateway. Run the installer again.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            ServiceCaller.Scheme, ServiceCaller.Server + ":" + key.Trim());
        if (settings is not null) request.Content = JsonContent.Create(new AdapterSettingsRequest(
            settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException(FailureCodes.DependencyTimeout, FailureKind.DependencyTimeout,
                "The integration gateway did not answer in time.", retryable: true);
        }
        catch (HttpRequestException exception)
        {
            throw new WellSiteAutoPilotException(FailureCodes.DependencyUnavailable, FailureKind.DependencyUnavailable,
                "The integration gateway could not be reached.", retryable: true, innerException: exception);
        }

        if (response.IsSuccessStatusCode || allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw Unavailable(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "The integration gateway did not accept the server's key."
            : "The integration gateway could not complete the request.");
    }

    private static WellSiteAutoPilotException Unavailable(string detail) =>
        new(FailureCodes.DependencyUnavailable, FailureKind.DependencyUnavailable, detail);
}
