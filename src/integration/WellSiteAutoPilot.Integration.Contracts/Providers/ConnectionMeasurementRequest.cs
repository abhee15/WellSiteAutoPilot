namespace WellSiteAutoPilot.Integration.Contracts.Providers;

// Server -> Gateway only; never a worker/module request DTO.
public sealed record ConnectionMeasurementRequest(Guid ConnectionId, int ConnectionRevision,
    Dictionary<string, string?> Settings, string ExternalId, string Quantity);
