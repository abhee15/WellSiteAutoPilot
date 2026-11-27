namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>What one ForeSite well reported for each catalogued surveillance quantity at one moment.</summary>
public sealed record ForeSiteSurveillanceReport(
    string AssetExternalId, IReadOnlyList<ForeSiteSurveillanceObservation> Observations, DateTimeOffset RetrievedAtUtc);

/// <summary>
/// The state of one catalogued field. <see cref="Unit"/> is the catalogued unit, or the unit the well reported
/// when it differs. A value present says nothing about whether the quantity applies to the well.
/// </summary>
public sealed record ForeSiteSurveillanceObservation(string Quantity, string? Unit, string Status)
{
    public const string Observed = "Observed";
    public const string NoValue = "NoValue";
    public const string Invalid = "Invalid";
    public const string UnitMismatch = "UnitMismatch";
}
