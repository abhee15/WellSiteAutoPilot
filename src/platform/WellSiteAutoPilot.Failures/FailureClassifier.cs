namespace WellSiteAutoPilot.Failures;

public static class FailureClassifier
{
    public static FailureDescriptor Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            WellSiteAutoPilotException known => new FailureDescriptor(
                known.Code,
                known.Kind,
                TitleFor(known.Kind),
                known.SafeDetail,
                known.Retryable),

            UnauthorizedAccessException => new FailureDescriptor(
                FailureCodes.AuthorizationDenied,
                FailureKind.Authorization,
                TitleFor(FailureKind.Authorization),
                "The requested operation is not permitted.",
                false),

            ArgumentException => new FailureDescriptor(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                TitleFor(FailureKind.Validation),
                "The request was invalid.",
                false),

            KeyNotFoundException => new FailureDescriptor(
                FailureCodes.ResourceNotFound,
                FailureKind.NotFound,
                TitleFor(FailureKind.NotFound),
                "The requested resource was not found.",
                false),

            TimeoutException => new FailureDescriptor(
                FailureCodes.DependencyTimeout,
                FailureKind.DependencyTimeout,
                TitleFor(FailureKind.DependencyTimeout),
                "A required dependency did not respond in time.",
                true),

            HttpRequestException => new FailureDescriptor(
                FailureCodes.DependencyUnavailable,
                FailureKind.DependencyUnavailable,
                TitleFor(FailureKind.DependencyUnavailable),
                "A required dependency is unavailable.",
                true),

            OperationCanceledException => new FailureDescriptor(
                FailureCodes.RequestCancelled,
                FailureKind.RequestCancelled,
                TitleFor(FailureKind.RequestCancelled),
                "The request was cancelled.",
                false),

            _ => new FailureDescriptor(
                FailureCodes.UnexpectedError,
                FailureKind.Unexpected,
                TitleFor(FailureKind.Unexpected),
                "An unexpected error occurred.",
                false)
        };
    }

    private static string TitleFor(FailureKind kind) => kind switch
    {
        FailureKind.Validation => "Request validation failed",
        FailureKind.NotFound => "Resource not found",
        FailureKind.Conflict => "Operation conflict",
        FailureKind.Authorization => "Operation not permitted",
        FailureKind.DependencyUnavailable => "Dependency unavailable",
        FailureKind.DependencyTimeout => "Dependency timeout",
        FailureKind.RequestCancelled => "Request cancelled",
        FailureKind.Unexpected => "Unexpected error",
        _ => "Unexpected error"
    };
}
