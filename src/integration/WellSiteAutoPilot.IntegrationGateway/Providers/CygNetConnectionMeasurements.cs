using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>Reads one current value for a saved CygNet connection, after the Server has authorized the read plan.</summary>
public sealed class CygNetConnectionMeasurements(ICygNetClient client)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task<EngineeringValue> ReadAsync(ConnectionMeasurementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
            CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection)
            throw new WellSiteAutoPilotException("CYGNET_CONNECTION_INVALID", FailureKind.Validation,
                "The saved CygNet connection is not valid.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        return await CygNetAdapter.ReadCurrentAsync(client, connection, request.ExternalId, request.Quantity, budget.Token);
    }
}
