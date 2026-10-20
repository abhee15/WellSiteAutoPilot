namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Connection settings sent to the Gateway to be checked or tried. Values are never logged or audited.</summary>
public sealed record AdapterSettingsRequest(Dictionary<string, string?>? Settings);

public sealed record AdapterValidationResponse(IReadOnlyList<AdapterSettingProblem> Problems);

/// <summary>The three honest answers a connection test can give.</summary>
public static class AdapterTestOutcomes
{
    public const string Healthy = "Healthy";
    public const string NeedsAttention = "NeedsAttention";
    public const string Unreachable = "Unreachable";

    public static bool IsKnown(string? outcome) => outcome is Healthy or NeedsAttention or Unreachable;
}

/// <summary>
/// What trying a connection found. <see cref="ActingAs"/> is the account the Gateway used, because that account,
/// not the person testing, is what the outside system sees.
/// </summary>
public sealed record AdapterTestResult(string Outcome, string Message, string? ActingAs);
