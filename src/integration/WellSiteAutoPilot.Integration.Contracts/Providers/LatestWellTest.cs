namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public sealed record LatestWellTestReadRequest(string AssetExternalId);

/// <summary>A provider's latest reported test. Its presence does not establish eligibility for evaluation.</summary>
public sealed record LatestWellTestResult(string AssetExternalId, WellTestRecord? Test, DateTimeOffset RetrievedAtUtc, string Source);

public sealed record WellTestRecord(string Id, DateTimeOffset? SampleDate, DateTimeOffset? TestEndDate,
    string? Status, WellTestValidation Validation)
{
    /// <summary>Recognized source fields with numeric values on this report. Presence conveys no eligibility or units.</summary>
    public IReadOnlyList<string>? ReportedQuantityFields { get; init; }
}

public sealed record WellTestValidation(string? ClassificationCode, string? ClassificationDescription,
    string? Code, string? Description);

public static class ForeSiteLatestWellTestContract
{
    public const string OperationKey = "well-test.latest-reported";
    public const string Version = "1.0";

    public static IReadOnlyList<string> QuantityFields { get; } = Array.AsReadOnly(new[]
    {
        "AverageCasingPressure", "AverageTubingPressure", "Oil", "Water", "Gas", "TotalFluid", "StrokePerMinute"
    });
}
