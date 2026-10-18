using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.System;

// Closed wire vocabulary. Member names are the JSON values; numeric values are
// private to the contract and 0 is deliberately undefined.
[JsonConverter(typeof(ClosedStringEnumConverter<PlatformComponentStatus>))]
public enum PlatformComponentStatus
{
    Unknown = 1,
    Healthy = 2,
    Degraded = 3,
    Unavailable = 4
}
