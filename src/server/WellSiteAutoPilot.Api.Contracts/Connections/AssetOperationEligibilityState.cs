using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Connections;

[JsonConverter(typeof(ClosedStringEnumConverter<AssetOperationEligibilityState>))]
public enum AssetOperationEligibilityState
{
    SourceCheckRequired = 1, ConnectionDisabled, ConnectionIncompatible, ConnectionTestRequired,
    ConnectionUnavailable, Unsupported, AssetInactive, Unmapped, AmbiguousSource, ResourceUnsupported
}
