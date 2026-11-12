using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>What the Server asks of the Gateway to resolve a named input against a saved connection.</summary>
public interface IGatewayResolver
{
    Task<ResolvePreview?> ResolveAsync(string adapterKey, ResolveRequest request, CancellationToken cancellationToken = default);
    Task<SetPointWritePlan?> PlanSetPointAsync(string adapterKey, SetPointPlanRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves, for one asset on one saved connection, which source serves a named input. The same call serves a
/// preview while authoring and the check at assignment and run time. Read-only: nothing here writes to a provider.
/// </summary>
public sealed class ConnectionResolveService(ConnectionService connections, IGatewayResolver gateway)
{
    public async Task<ResolvePreview> ResolveAsync(
        Guid connectionId, string externalId, string input, ResolveBasis? basis, bool read, CancellationToken cancellationToken = default)
    {
        var connection = await Enabled(connectionId, cancellationToken);
        var request = new ResolveRequest(connection.Id, connection.Revision, Settings(connection), externalId, input, basis, read);
        return await gateway.ResolveAsync(connection.AdapterKey, request, cancellationToken)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryUnsupported, FailureKind.Conflict,
                "This connection's adapter cannot resolve inputs yet.");
    }

    public async Task<SetPointWritePlan> PlanSetPointAsync(
        Guid connectionId, string externalId, string setPoint, decimal delta, CancellationToken cancellationToken = default)
    {
        var connection = await Enabled(connectionId, cancellationToken);
        var request = new SetPointPlanRequest(connection.Id, connection.Revision, Settings(connection), externalId, setPoint, delta);
        return await gateway.PlanSetPointAsync(connection.AdapterKey, request, cancellationToken)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryUnsupported, FailureKind.Conflict,
                "This connection's adapter has no set points to plan against.");
    }

    private async Task<ConnectionRecord> Enabled(Guid id, CancellationToken cancellationToken)
    {
        var connection = await connections.GetAsync(id, cancellationToken);
        return connection.IsEnabled ? connection
            : throw new WellSiteAutoPilotException(ConnectionFailureCodes.Disabled, FailureKind.Conflict, "Enable the connection first.");
    }

    private static Dictionary<string, string?> Settings(ConnectionRecord connection) =>
        connection.Settings.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.Ordinal);
}
