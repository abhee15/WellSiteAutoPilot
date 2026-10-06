namespace WellSiteAutoPilot.Failures;

public enum FailureKind
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    Authorization = 3,
    DependencyUnavailable = 4,
    DependencyTimeout = 5,
    RequestCancelled = 6,
    Unexpected = 7
}
