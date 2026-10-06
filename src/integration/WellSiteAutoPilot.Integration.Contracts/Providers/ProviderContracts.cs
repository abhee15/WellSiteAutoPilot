namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public enum ProviderHealthState
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3
}

public enum ProviderCompatibilityState
{
    Unknown = 0,
    Compatible = 1,
    CompatibleWithWarnings = 2,
    Incompatible = 3
}

public enum ProviderCapabilityKind
{
    CurrentData = 0,
    HistoricalData = 1,
    Alarms = 2,
    AssetDiscovery = 3,
    Calculation = 4,
    Command = 5,
    Verification = 6
}

public sealed record ProviderHealth(
    string ProviderId,
    string DisplayName,
    ProviderHealthState State,
    string? Reason = null,
    DateTimeOffset? LastSuccessfulOperationAt = null);

public sealed record ProviderCapability(
    ProviderCapabilityKind Kind,
    string Name,
    string ContractVersion = "1");

public sealed record ProviderDescriptor(
    string ProviderId,
    string DisplayName,
    string ProviderKind,
    string AdapterVersion,
    string? ProviderVersion,
    IReadOnlyCollection<ProviderCapability> Capabilities);

public sealed record ProviderCompatibility(
    string ProviderId,
    ProviderCompatibilityState State,
    string AdapterVersion,
    string? ProviderVersion,
    string? Reason = null);

public sealed record EngineeringValue(
    string Quantity,
    decimal Value,
    string Unit,
    DateTimeOffset TimestampUtc,
    string Quality,
    string Source);

public sealed record ProviderAlarm(
    string AlarmId,
    string AssetExternalId,
    string Code,
    string Severity,
    string State,
    DateTimeOffset TimestampUtc,
    string Source);

public sealed record DiscoveredAsset(
    string ExternalId,
    string DisplayName,
    string ProviderType,
    string? ParentExternalId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ProviderCommandRequest(
    Guid ControlActionId,
    string AssetExternalId,
    string Command,
    decimal? Value,
    string? Unit,
    string IdempotencyKey);

public sealed record ProviderCommandResult(
    Guid ControlActionId,
    string ProviderRequestId,
    string Status,
    DateTimeOffset SentAtUtc,
    string? Detail = null);

public sealed record ProviderVerificationRequest(
    Guid ControlActionId,
    string AssetExternalId,
    string Command,
    string? Quantity,
    decimal? ExpectedValue,
    string? Unit);

public sealed record ProviderVerificationResult(
    Guid ControlActionId,
    bool Verified,
    EngineeringValue? Readback,
    string? Detail = null);

public interface IProviderHealthProvider
{
    ValueTask<ProviderHealth> GetHealthAsync(
        CancellationToken cancellationToken = default);
}

public interface IProviderMetadataProvider
{
    ValueTask<ProviderDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ProviderCompatibility> GetCompatibilityAsync(
        CancellationToken cancellationToken = default);
}

public interface ICurrentDataProvider
{
    ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default);
}

public interface IHistoricalDataProvider
{
    ValueTask<IReadOnlyCollection<EngineeringValue>> GetHistoryAsync(
        string assetExternalId,
        string quantity,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);
}

public interface IAlarmProvider
{
    ValueTask<IReadOnlyCollection<ProviderAlarm>> GetAlarmsAsync(
        string assetExternalId,
        DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default);
}

public interface IAssetDiscoveryProvider
{
    ValueTask<IReadOnlyCollection<DiscoveredAsset>> DiscoverAssetsAsync(
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
}

public interface ICalculationProvider
{
    ValueTask<IReadOnlyDictionary<string, decimal>> CalculateAsync(
        string calculation,
        IReadOnlyDictionary<string, decimal> inputs,
        CancellationToken cancellationToken = default);
}

public interface ICommandProvider
{
    ValueTask<ProviderCommandResult> ExecuteAsync(
        ProviderCommandRequest request,
        CancellationToken cancellationToken = default);
}

public interface IVerificationProvider
{
    ValueTask<ProviderVerificationResult> VerifyAsync(
        ProviderVerificationRequest request,
        CancellationToken cancellationToken = default);
}
