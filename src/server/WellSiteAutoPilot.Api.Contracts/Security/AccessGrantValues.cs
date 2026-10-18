using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Security;

// Closed wire vocabulary. Member names are the JSON values. Numeric values are
// private to the contract (0 is deliberately undefined so an omitted property
// never silently becomes a real value); the server maps by name.
[JsonConverter(typeof(ClosedStringEnumConverter<AccessGrantSubjectKind>))]
public enum AccessGrantSubjectKind
{
    User = 1,
    WindowsGroup = 2
}

[JsonConverter(typeof(ClosedStringEnumConverter<AccessGrantScopeKind>))]
public enum AccessGrantScopeKind
{
    ExactAssets = 1,
    DeploymentWide = 2,
    AllAssets = 3
}
