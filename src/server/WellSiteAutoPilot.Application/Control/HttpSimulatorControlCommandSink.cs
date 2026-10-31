using System.Net.Http.Json;
using System.Text.Json;
using WellSiteAutoPilot.Domain.Control;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>
/// Simulator sink that talks HTTP to the Provider Simulator command endpoint. It is still a simulator: the
/// base address must be the simulator, and real provider writes are only ever refused elsewhere.
/// </summary>
public sealed class HttpSimulatorControlCommandSink(HttpClient client) : IControlCommandSink
{
    private sealed record CommandBody(string IdempotencyKey, string AssetId, string Command, string? Quantity,
        decimal Value, string? Unit);

    private sealed record CommandResultBody(string Reference, string State, decimal? ObservedValue);

    public string Kind => ControlSinkKinds.Simulator;

    public async Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Value is not { } value)
        {
            return new ControlDispatchResult(false, "SIMULATOR_VALUE_REQUIRED", "A command value is required.");
        }

        using var response = await client.PostAsJsonAsync("/api/sim/v1/commands",
            new CommandBody(request.IdempotencyKey, request.AssetId.ToString(), request.Command, request.Quantity, value, request.Unit),
            JsonSerializerOptions.Web, cancellationToken);
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            return new ControlDispatchResult(false, $"SIMULATOR_HTTP_{(int)response.StatusCode}", "The simulator refused the command.");
        }

        // 5xx / transport failures throw: the caller records the action as Ambiguous rather than Failed.
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CommandResultBody>(JsonSerializerOptions.Web, cancellationToken);
        return new ControlDispatchResult(true, body?.Reference ?? $"sim-http:{request.ActionId:N}");
    }

    public async Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            $"/api/sim/v1/commands/{Uri.EscapeDataString(request.IdempotencyKey)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CommandResultBody>(JsonSerializerOptions.Web, cancellationToken);
        return body?.State switch
        {
            "Applied" => new ControlReadback(ControlReadbackState.Applied, body.ObservedValue),
            "NotApplied" => new ControlReadback(ControlReadbackState.NotApplied),
            _ => new ControlReadback(ControlReadbackState.Unknown)
        };
    }
}
