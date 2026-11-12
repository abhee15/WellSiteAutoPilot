using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.Integration.CygNet;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>Reads one current value for a saved CygNet connection, after the Server has authorized the read plan.</summary>
public sealed class CygNetConnectionMeasurements(ICygNetClient client)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task<HistoricalReadResult> ReadHistoryAsync(ConnectionHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HistoricalReadLimits.Validate(request.Read);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
            CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection)
            throw new WellSiteAutoPilotException("CYGNET_CONNECTION_INVALID", FailureKind.Validation, "The saved CygNet connection is not valid.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        try { return await new CygNetHistoricalDataProvider(client, connection).ReadHistoryAsync(request.Read, budget.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException("CYGNET_READ_TIMEOUT", FailureKind.DependencyTimeout, "CygNet did not return history in time.");
        }
    }

    public async Task<ResolvePreview> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
            CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection)
            throw new WellSiteAutoPilotException("CYGNET_CONNECTION_INVALID", FailureKind.Validation, "The saved CygNet connection is not valid.");
        var quantities = new Dictionary<string, CygNetQuantityMapping>(CygNetProviderOptions.DefaultQuantities, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mapping) in connection.PointMap ?? new Dictionary<string, CygNetPointMapping>())
            quantities[name] = new CygNetQuantityMapping(mapping.Udc, mapping.Unit);
        var provider = new CygNetResolveProvider(new CygNetProviderOptions((ushort)connection.Domain, quantities));
        var tag = connection.TagFor(request.ExternalId);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        try
        {
            var result = await provider.ResolveAsync(tag, request.Input, request.Basis, budget.Token);
            if (!request.Read || result.Outcome != ResolveOutcome.Compatible)
                return new ResolvePreview(result, null, null);
            try { return new ResolvePreview(result, await provider.ReadAsync(tag, result.Candidates[0], budget.Token), null); }
            catch (WellSiteAutoPilotException error) { return new ResolvePreview(result, null, error.Code); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException("CYGNET_READ_TIMEOUT", FailureKind.DependencyTimeout, "CygNet did not answer in time.");
        }
    }

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
        return await new CygNetConnectionDataProvider(client, connection).GetCurrentAsync(request.ExternalId, request.Quantity, budget.Token);
    }
}
