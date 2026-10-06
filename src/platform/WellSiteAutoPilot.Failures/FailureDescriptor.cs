namespace WellSiteAutoPilot.Failures;

public sealed record FailureDescriptor(
    string Code,
    FailureKind Kind,
    string Title,
    string Detail,
    bool Retryable);
