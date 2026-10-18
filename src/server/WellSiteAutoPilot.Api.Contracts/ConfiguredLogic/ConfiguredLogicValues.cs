using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;

// Closed wire vocabulary. Member names are the JSON values; numeric values are
// private to the contract and 0 is deliberately undefined.
[JsonConverter(typeof(ClosedStringEnumConverter<ConfiguredLogicRevisionStatus>))]
public enum ConfiguredLogicRevisionStatus
{
    Draft = 1,
    Validated = 2,
    Active = 3,
    Superseded = 4
}
