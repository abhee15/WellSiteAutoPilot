using System.Net.Http.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>
/// Sends one command to the simulated provider, once. The client behind it must never retry: the simulator, like a real
/// provider, has no idempotency, so a repeated send would apply twice. Only a definite answer is reported; everything
/// else is thrown as a dependency failure, which the caller treats as ambiguous.
/// </summary>
public sealed class SimulatorCommandProvider(HttpClient client)
{
    public const string ClientName = "simulator-command";

    public async Task<ProviderCommandResponse> SendAsync(ProviderCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExternalId) || string.IsNullOrWhiteSpace(request.Command) ||
            string.IsNullOrWhiteSpace(request.RequestId))
            throw new WellSiteAutoPilotException("COMMAND_REQUEST_INVALID", FailureKind.Validation,
                "A command needs an asset, a command name and a request id.");

        HttpResponseMessage response;
        try
        {
            response = await client.PutAsJsonAsync(
                $"/api/sim/v1/assets/{Uri.EscapeDataString(request.ExternalId)}/commands/{Uri.EscapeDataString(request.Command)}",
                new { requestId = request.RequestId, value = request.Value, unit = request.Unit },
                cancellationToken);
        }
        catch (HttpRequestException) { throw Unknown("SIMULATOR_COMMAND_UNREACHABLE"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException("SIMULATOR_COMMAND_TIMEOUT", FailureKind.DependencyTimeout,
                "The provider did not answer the command in time. It may or may not have acted.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return new(ProviderCommandOutcome.Sent, null, await ReferenceAsync(response, cancellationToken));
            // Only a definite refusal is a rejection. A server error says nothing about whether the provider acted.
            if ((int)response.StatusCode is 404 or 422)
                return new(ProviderCommandOutcome.Rejected, await CodeAsync(response, cancellationToken) ?? "REJECTED_BY_PROVIDER", null);
            throw Unknown("SIMULATOR_COMMAND_RESULT_UNKNOWN");
        }
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken);
            return body is not null && body.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code) ? code : null;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<string?> ReferenceAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken);
            return body is not null && body.TryGetValue("reference", out var reference) ? reference : null;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or NotSupportedException or HttpRequestException)
        {
            // The provider took the command; a body we cannot read does not change that.
            return null;
        }
    }

    private static WellSiteAutoPilotException Unknown(string code) => new(code, FailureKind.DependencyUnavailable,
        "The provider's answer to the command is unknown. It may or may not have acted.");
}
