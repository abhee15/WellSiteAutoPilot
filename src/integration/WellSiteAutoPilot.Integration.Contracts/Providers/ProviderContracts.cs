namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public enum ProviderHealthState
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3
}

public sealed record ProviderHealth(
    string ProviderId,
    string DisplayName,
    ProviderHealthState State,
    string? Reason = null,
    DateTimeOffset? LastSuccessfulOperationAt = null);

public sealed record EngineeringValue(
    string Quantity,
    decimal Value,
    string Unit,
    DateTimeOffset TimestampUtc,
    string Quality,
    string Source);

public interface IProviderHealthProvider
{
    ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentDataProvider
{
    ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default);
}

public interface ICalculationProvider
{
    ValueTask<IReadOnlyDictionary<string, decimal>> CalculateAsync(
        string calculation,
        IReadOnlyDictionary<string, decimal> inputs,
        CancellationToken cancellationToken = default);
}
