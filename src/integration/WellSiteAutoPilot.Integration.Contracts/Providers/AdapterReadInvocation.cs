namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Server-authorized request for an installed read operation on an imported connection resource.</summary>
public sealed record AdapterReadInvocation<TInput>(Guid ConnectionId, int ConnectionRevision,
    IReadOnlyDictionary<string, string?> Settings, string ResourceKind, string ExternalId, string ContractVersion, TInput Input);
