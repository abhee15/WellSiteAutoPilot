using System.Net.Http.Headers;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Server.Control;

/// <summary>
/// The Provider sink: sends one command to the Integration Gateway and asks for the value back. It is the only place the
/// Server speaks a command to the Gateway.
/// <para>
/// A command is sent <b>once</b>. The HTTP client behind this sink must never carry a retry or resilience handler (the
/// convention check enforces it), because a repeated send applies the command twice. Only a closed Gateway answer is
/// treated as known: <c>Sent</c> and <c>Rejected</c>. A timeout, a transport error, a server error or an unreadable
/// answer throws, and the gate records that as ambiguous without ever retrying.
/// </para>
/// </summary>
public sealed class GatewayControlCommandSink(HttpClient client, IConfiguration configuration, IGatewayAdapters adapters)
    : IControlCommandSink
{
    public const string ClientName = nameof(GatewayControlCommandSink);

    private const string Base = "api/internal/v1/adapters";

    public string Kind => ControlSinkKinds.Provider;

    public async Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = request.Target ?? throw new InvalidOperationException("A provider command needs a resolved target.");
        if (request.Value is not { } value) throw new InvalidOperationException("A provider command needs a value.");

        var body = new GatewayCommandRequest(target.ConnectionId, target.ConnectionRevision, Copy(target.Settings),
            target.ExternalId, request.Command, request.IdempotencyKey, value, request.Unit,
            target.Entry.Target.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.Ordinal));

        var answer = await PostAsync<GatewayCommandResponse>(target.AdapterKey, "command", body, cancellationToken);
        switch (answer.Outcome)
        {
            case GatewayCommandOutcome.Sent:
                return new ControlDispatchResult(true, Reference(answer.Reference, request));
            case GatewayCommandOutcome.Rejected:
                // The provider definitely did not act. Only its short code is kept; its text stays in the Gateway.
                return new ControlDispatchResult(false, SafeCode(answer.Code));
            default:
                // DryRun is not a send, and an outcome this build does not know is not an answer.
                throw Ambiguous("The integration gateway gave an answer that is not a send.");
        }
    }

    public async Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not { } target) return new ControlReadback(ControlReadbackState.Unknown);

        var descriptor = (await adapters.ListAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, target.AdapterKey, StringComparison.Ordinal));
        var quantity = descriptor?.Commands?
            .FirstOrDefault(item => string.Equals(item.Name, request.Command, StringComparison.Ordinal))?.ReadbackQuantity;
        // A command the adapter cannot read back (acknowledgement only) is never guessed at.
        if (string.IsNullOrWhiteSpace(quantity)) return new ControlReadback(ControlReadbackState.Unknown);

        var observed = await PostAsync<EngineeringValue>(target.AdapterKey, "command-readback",
            new ConnectionMeasurementRequest(target.ConnectionId, target.ConnectionRevision, Copy(target.Settings),
                target.ExternalId, quantity), cancellationToken);
        return ReadbackFrom(observed, request.Unit);
    }

    /// <summary>
    /// A value counts only when it is a good reading in the unit that was commanded. Anything else is "cannot say":
    /// a wrong Applied or NotApplied is a wrong statement about physical equipment.
    /// </summary>
    public static ControlReadback ReadbackFrom(EngineeringValue? observed, string? commandedUnit)
    {
        if (observed is not { Value: { } number }) return new ControlReadback(ControlReadbackState.Unknown);
        if (!string.Equals(observed.Quality, "Good", StringComparison.OrdinalIgnoreCase))
            return new ControlReadback(ControlReadbackState.Unknown);
        if (commandedUnit is not null && !string.Equals(observed.Unit, commandedUnit, StringComparison.OrdinalIgnoreCase))
            return new ControlReadback(ControlReadbackState.Unknown);
        return new ControlReadback(ControlReadbackState.Applied, number);
    }

    private async Task<T> PostAsync<T>(string adapterKey, string route, object body, CancellationToken cancellationToken)
    {
        var key = configuration[ServiceCallerClient.KeyConfigurationName];
        if (string.IsNullOrWhiteSpace(key)) throw Ambiguous("The server has no key for the integration gateway.");

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(adapterKey)}/{route}")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(ServiceCaller.Scheme, ServiceCaller.Server + ":" + key.Trim());

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException(FailureCodes.DependencyTimeout, FailureKind.DependencyTimeout,
                "The integration gateway did not answer in time.", retryable: false);
        }
        catch (HttpRequestException exception)
        {
            throw new WellSiteAutoPilotException(FailureCodes.DependencyUnavailable, FailureKind.DependencyUnavailable,
                "The integration gateway could not be reached.", retryable: false, innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Ambiguous("The integration gateway could not complete the request.");
            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                    ?? throw Ambiguous("The integration gateway answer was empty.");
            }
            catch (System.Text.Json.JsonException)
            {
                throw Ambiguous("The integration gateway answer could not be read.");
            }
        }
    }

    private static Dictionary<string, string?> Copy(IReadOnlyDictionary<string, string?> source) =>
        source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static string Reference(string? reference, ControlDispatchRequest request) =>
        reference is { Length: > 0 and <= 128 } ? reference : $"gateway:{request.ActionId:N}";

    // Codes are short upper-case words; anything else could carry provider text and is replaced.
    private static string SafeCode(string? code) =>
        code is { Length: > 0 and <= 128 } && char.IsAsciiLetterUpper(code[0])
        && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
            ? code
            : "PROVIDER_REJECTED";

    private static WellSiteAutoPilotException Ambiguous(string detail) =>
        new(FailureCodes.DependencyUnavailable, FailureKind.DependencyUnavailable, detail);
}
