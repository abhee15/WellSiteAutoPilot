namespace WellSiteAutoPilot.Domain.Recommendations;

public enum RecommendationStatus
{
    Created = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
    Superseded = 5,
    Invalidated = 6,
    Executed = 7
}
