using WellSiteAutoPilot.Failures;

var cases = new[]
{
    new Case(
        new ArgumentException("internal validation detail"),
        FailureCodes.ValidationFailed,
        FailureKind.Validation,
        false,
        "The request was invalid."),
    new Case(
        new KeyNotFoundException("sensitive lookup key"),
        FailureCodes.ResourceNotFound,
        FailureKind.NotFound,
        false,
        "The requested resource was not found."),
    new Case(
        new UnauthorizedAccessException("internal authorization rule"),
        FailureCodes.AuthorizationDenied,
        FailureKind.Authorization,
        false,
        "The requested operation is not permitted."),
    new Case(
        new TimeoutException("socket timeout detail"),
        FailureCodes.DependencyTimeout,
        FailureKind.DependencyTimeout,
        true,
        "A required dependency did not respond in time."),
    new Case(
        new HttpRequestException("provider host detail"),
        FailureCodes.DependencyUnavailable,
        FailureKind.DependencyUnavailable,
        true,
        "A required dependency is unavailable."),
    new Case(
        new InvalidOperationException("must never leak"),
        FailureCodes.UnexpectedError,
        FailureKind.Unexpected,
        false,
        "An unexpected error occurred.")
};

foreach (var item in cases)
{
    var actual = FailureClassifier.Classify(item.Exception);

    AssertEqual(item.Code, actual.Code, nameof(actual.Code));
    AssertEqual(item.Kind, actual.Kind, nameof(actual.Kind));
    AssertEqual(item.Retryable, actual.Retryable, nameof(actual.Retryable));
    AssertEqual(item.Detail, actual.Detail, nameof(actual.Detail));

    if (actual.Detail.Contains(item.Exception.Message, StringComparison.Ordinal) &&
        item.Exception is not WellSiteAutoPilotException)
    {
        throw new InvalidOperationException(
            $"Failure classifier leaked the original exception message for {item.Exception.GetType().Name}.");
    }
}

var known = new WellSiteAutoPilotException(
    "DATA_STALE",
    FailureKind.Validation,
    "Required engineering data is stale.");

var knownDescriptor = FailureClassifier.Classify(known);
AssertEqual("DATA_STALE", knownDescriptor.Code, nameof(knownDescriptor.Code));
AssertEqual("Required engineering data is stale.", knownDescriptor.Detail, nameof(knownDescriptor.Detail));

Console.WriteLine("Failure contract checks passed.");
return 0;

static void AssertEqual<T>(T expected, T actual, string member)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Failure contract assertion failed for {member}. Expected '{expected}', actual '{actual}'.");
    }
}

internal sealed record Case(
    Exception Exception,
    string Code,
    FailureKind Kind,
    bool Retryable,
    string Detail);
