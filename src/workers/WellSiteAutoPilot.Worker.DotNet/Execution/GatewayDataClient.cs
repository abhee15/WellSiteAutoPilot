using System.Net.Http.Json;
using System.Text.Json;
using NATS.Client.Core;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class GatewayDataClient(HttpClient httpClient, INatsConnection? connection = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EngineeringValue> GetCurrentAsync(Guid executionId, ExecutionInputBindingV2 input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Source is null)
        {
            if (!string.Equals(input.ProviderId, "simulator", StringComparison.OrdinalIgnoreCase))
                throw ReadFailure("EXECUTION_READ_PLAN_REQUIRED");
            return await GetCurrentAsync(input.ProviderId, input.ProviderAssetExternalId, input.Quantity, cancellationToken);
        }
        if (connection is null) throw ReadFailure("EXECUTION_READ_PLAN_UNAVAILABLE");
        await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
        if (connection.ServerInfo?.AuthRequired != true) throw ReadFailure("EXECUTION_READ_BROKER_AUTH_REQUIRED");
        // Routing fields on the worker message are never sent as authority.
        var response = await connection.RequestAsync<string, string>(Subjects.ExecutionReadCurrentV1,
            JsonSerializer.Serialize(new ExecutionReadCurrentV1(executionId, input.Source.ReadPlanId), JsonOptions),
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) }, cancellationToken: cancellationToken);
        var reply = response.Data is { } json && System.Text.Encoding.UTF8.GetByteCount(json) <= MeasurementPayloadLimits.MaximumReplyBytes
            ? JsonSerializer.Deserialize<ExecutionReadReplyV1>(json, JsonOptions) : null;
        if (reply is null || (reply.Value is null) == (reply.FailureCode is null)) throw ReadFailure("EXECUTION_READ_REPLY_INVALID");
        if (reply.FailureCode is { } code) throw ReadFailure(code);
        if (!MeasurementPayloadLimits.IsValid(reply.Value!)) throw ReadFailure("EXECUTION_READ_REPLY_INVALID");
        return reply.Value!;
    }

    private static WellSiteAutoPilotException ReadFailure(string code) => new(code, FailureKind.DependencyUnavailable,
        "The authorized execution input could not be acquired.");
    public async Task<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default) =>
        await GetCurrentAsync(null, assetExternalId, quantity, cancellationToken);

    public async Task<EngineeringValue> GetCurrentAsync(
        string? providerId,
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);

        var url =
            $"/api/internal/v1/data/current?assetExternalId={Uri.EscapeDataString(assetExternalId)}" +
            $"&quantity={Uri.EscapeDataString(quantity)}";

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            url += $"&providerId={Uri.EscapeDataString(providerId)}";
        }

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EngineeringValue>(
            cancellationToken: cancellationToken) ??
            throw new InvalidOperationException(
                "The Integration Gateway returned an empty engineering value.");
    }
}
