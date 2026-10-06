namespace WellSiteAutoPilot.Failures;

public static class FailureCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string AuthorizationDenied = "AUTHORIZATION_DENIED";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string DependencyTimeout = "DEPENDENCY_TIMEOUT";
    public const string RequestCancelled = "REQUEST_CANCELLED";
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}
