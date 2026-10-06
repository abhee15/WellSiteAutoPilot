namespace WellSiteAutoPilot.Failures;

public sealed class WellSiteAutoPilotException : Exception
{
    public WellSiteAutoPilotException(
        string code,
        FailureKind kind,
        string safeDetail,
        bool retryable = false,
        Exception? innerException = null)
        : base(safeDetail, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeDetail);

        Code = code;
        Kind = kind;
        SafeDetail = safeDetail;
        Retryable = retryable;
    }

    public string Code { get; }

    public FailureKind Kind { get; }

    public string SafeDetail { get; }

    public bool Retryable { get; }
}
