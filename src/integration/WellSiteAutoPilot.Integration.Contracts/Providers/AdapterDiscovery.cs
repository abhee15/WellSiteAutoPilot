namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// What an adapter found. <see cref="Complete"/> is false for any partial or failed answer, and only a complete
/// result may be used to say an object has gone missing. <see cref="Warnings"/> are plain sentences for the person;
/// they never carry anything the provider sent back verbatim. <see cref="ActingAs"/> is the account that was used.
/// </summary>
public sealed record DiscoveryResult(
    bool Complete,
    IReadOnlyList<DiscoveredAsset> Items,
    IReadOnlyList<string> Warnings,
    string? ActingAs);
