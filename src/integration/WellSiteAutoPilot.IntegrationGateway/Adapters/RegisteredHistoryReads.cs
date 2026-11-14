using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class RegisteredHistoryReads
{
    public static AdapterRegistration Register(AdapterDescriptor descriptor,
        Func<ConnectionHistoryRequest, IServiceProvider, CancellationToken, Task<HistoricalReadResult>> read)
    {
        var reads = descriptor.ReadOperations()
            .Where(operation => operation.CapabilityId == "engineering.history")
            .Select(operation => AdapterReadRegistration.Create<HistoricalReadRequest, HistoricalReadResult>(
                operation, (request, invocation, services, cancellationToken) =>
                {
                    HistoricalReadLimits.Validate(request);
                    if (request.Quantity != operation.Quantity || request.AssetExternalId != invocation.ExternalId ||
                        request.EndUtc - request.StartUtc > TimeSpan.FromDays(operation.Limits.MaximumWindowDays!.Value) ||
                        request.MaximumSamples > operation.Limits.MaximumSamples!.Value)
                        throw AdapterReadEndpoints.InvalidRequest();

                    return read(new(invocation.ConnectionId, invocation.ConnectionRevision, invocation.Settings.ToDictionary(StringComparer.Ordinal), request),
                        services, cancellationToken);
                })).ToArray();
        return new(descriptor, reads);
    }
}
