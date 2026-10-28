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
    string Source,
    DateTimeOffset? RetrievedAtUtc = null,
    string? TimestampKind = null)
{
    // POC: structured (array/object) payloads. Value is 0 and Unit is "structured" when these are set.
    public string? StructuredSchemaId { get; init; }
    public string? StructuredJson { get; init; }
}

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

/// <summary>Routes engineering reads to the explicitly selected provider without
/// falling back to a different data source when an identifier is unknown.</summary>
public sealed class CurrentDataProviderRouter
{
    private readonly Dictionary<string, ICurrentDataProvider> _providers;
    private readonly string _defaultProviderId;

    public CurrentDataProviderRouter(
        IEnumerable<KeyValuePair<string, ICurrentDataProvider>> providers,
        string defaultProviderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProviderId);
        var entries = providers.ToArray();
        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
        {
            throw new ArgumentException("Provider registrations require an identifier and adapter.", nameof(providers));
        }

        _providers = entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
        if (!_providers.ContainsKey(defaultProviderId))
        {
            throw new ArgumentException("The default provider must be registered.", nameof(defaultProviderId));
        }

        _defaultProviderId = defaultProviderId;
    }

    public ValueTask<EngineeringValue> GetCurrentAsync(
        string? providerId,
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        var selectedId = string.IsNullOrWhiteSpace(providerId)
            ? _defaultProviderId
            : providerId;
        if (!_providers.TryGetValue(selectedId, out var provider))
        {
            throw new ArgumentException(
                $"Unknown current-data provider '{selectedId}'.", nameof(providerId));
        }

        return provider.GetCurrentAsync(assetExternalId, quantity, cancellationToken);
    }
}


public sealed class FactoryCurrentDataProvider(Func<ICurrentDataProvider> createProvider) : ICurrentDataProvider
{
    public ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default) =>
        createProvider().GetCurrentAsync(assetExternalId, quantity, cancellationToken);
}

public sealed class CircuitBreakingCurrentDataProvider : ICurrentDataProvider
{
    private readonly ICurrentDataProvider _inner;
    private readonly TimeProvider _clock;
    private readonly int _failureThreshold;
    private readonly TimeSpan _recoveryDelay;
    private readonly object _gate = new();
    private int _failures;
    private DateTimeOffset? _openUntil;
    private bool _probeInProgress;

    public CircuitBreakingCurrentDataProvider(
        ICurrentDataProvider inner,
        TimeProvider clock,
        int failureThreshold = 3,
        TimeSpan? recoveryDelay = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _failureThreshold = failureThreshold > 0
            ? failureThreshold : throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        _recoveryDelay = recoveryDelay ?? TimeSpan.FromSeconds(30);
        if (_recoveryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recoveryDelay));
    }

    public async ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool isProbe = false;
        lock (_gate)
        {
            if (_openUntil is { } until)
            {
                if (_clock.GetUtcNow() < until || _probeInProgress)
                    throw new InvalidOperationException("Current-data provider circuit is open.");
                _probeInProgress = true;
                isProbe = true;
            }
        }

        try
        {
            var result = await _inner.GetCurrentAsync(assetExternalId, quantity, cancellationToken);
            lock (_gate)
            {
                // A successful ordinary request cannot close a circuit opened
                // by another concurrent request; only the recovery probe can.
                if (isProbe || _openUntil is null)
                {
                    _failures = 0;
                    _openUntil = null;
                }
                if (isProbe) _probeInProgress = false;
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (isProbe)
                {
                    _openUntil = _clock.GetUtcNow() + _recoveryDelay;
                    _probeInProgress = false;
                }
            }
            throw;
        }
        catch
        {
            lock (_gate)
            {
                if (isProbe)
                {
                    _openUntil = _clock.GetUtcNow() + _recoveryDelay;
                    _probeInProgress = false;
                }
                else if (_openUntil is null && ++_failures >= _failureThreshold)
                {
                    _openUntil = _clock.GetUtcNow() + _recoveryDelay;
                }
            }
            throw;
        }
    }
}
