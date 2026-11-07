namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>One source identity, already selected/authorized by the caller. No discovery or arbitrary provider query.</summary>
public sealed record AssetConfigurationReadRequest(string AssetExternalId);

/// <summary>Identity/classification configuration only. No provider DTO, engineering model, telemetry or well-test data.</summary>
public sealed record AssetConfigurationDetails(string? ClassificationCode, string? FacilityId, string? Field,
    string? Lease, string? DeviceType);
public sealed record AssetConfigurationResult(string AssetExternalId, string DisplayName, string ResourceKind,
    AssetConfigurationDetails Details, DateTimeOffset? SourceChangedAtUtc, DateTimeOffset RetrievedAtUtc, string Source);

public interface IAssetConfigurationProvider
{
    ValueTask<AssetConfigurationResult> ReadConfigurationAsync(AssetConfigurationReadRequest request,
        CancellationToken cancellationToken = default);
}

// Server -> Gateway only. These private Connection settings are never part of catalog examples or worker inputs.
public sealed record ConnectionConfigurationRequest(Guid ConnectionId, int ConnectionRevision,
    Dictionary<string, string?> Settings, AssetConfigurationReadRequest Read);
