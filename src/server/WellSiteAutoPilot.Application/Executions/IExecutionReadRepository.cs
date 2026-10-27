namespace WellSiteAutoPilot.Application.Executions;

// Server/Gateway boundary only. Never return this projection to workers, modules or public APIs.
public sealed record AuthorizedExecutionRead(string AdapterKey, Guid ConnectionId, int ConnectionRevision,
    string ExternalId, string Quantity, IReadOnlyDictionary<string, string?> Settings);

public interface IExecutionReadRepository
{
    Task<AuthorizedExecutionRead> ResolveReadAsync(Guid executionId, Guid readPlanId,
        CancellationToken cancellationToken = default);
}

public interface IExecutionMeasurementGateway
{
    Task<WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue> GetCurrentAsync(
        AuthorizedExecutionRead read, CancellationToken cancellationToken = default);
}
