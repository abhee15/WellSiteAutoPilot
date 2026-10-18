using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Recommendations;

// Closed wire vocabulary. Member names are the JSON values; numeric values are
// private to the contract and 0 is deliberately undefined.
[JsonConverter(typeof(ClosedStringEnumConverter<RecommendationStatus>))]
public enum RecommendationStatus
{
    Created = 1,
    Pending = 2,
    Approved = 3,
    Rejected = 4,
    Expired = 5,
    Superseded = 6,
    Invalidated = 7,
    Executed = 8
}

/// <summary>The only operator decisions a request may submit.</summary>
[JsonConverter(typeof(ClosedStringEnumConverter<RecommendationDecision>))]
public enum RecommendationDecision
{
    Approved = 1,
    Rejected = 2
}
