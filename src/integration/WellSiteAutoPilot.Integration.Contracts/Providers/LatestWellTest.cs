namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public sealed record LatestWellTestReadRequest(string AssetExternalId);

/// <summary>A provider's latest reported test. Its presence does not establish eligibility for evaluation.</summary>
public sealed record LatestWellTestResult(string AssetExternalId, WellTestRecord? Test, DateTimeOffset RetrievedAtUtc, string Source);

public sealed record WellTestRecord(string Id, DateTimeOffset? SampleDate, DateTimeOffset? TestEndDate,
    string? Status, WellTestValidation Validation);

public sealed record WellTestValidation(string? ClassificationCode, string? ClassificationDescription,
    string? Code, string? Description);
