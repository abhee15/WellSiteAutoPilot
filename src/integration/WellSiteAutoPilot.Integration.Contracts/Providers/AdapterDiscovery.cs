namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// What an adapter found. <see cref="Complete"/> is false for any partial or failed answer, and only a complete
/// result may be used to say an object has gone missing. <see cref="Warnings"/> are plain sentences for the person;
/// they never carry anything the provider sent back verbatim. <see cref="ActingAs"/> is the account that was used.
/// <see cref="UnavailableFields"/> marks facts that could not be read; blank values from a successful read are different.
/// Null means the adapter has not declared availability. SourceScope is the authorized source asset filter,
/// including unmapped scope, when the adapter can supply it; it is not inferred from returned wells.
/// </summary>
public sealed record DiscoveryResult(
    bool Complete,
    IReadOnlyList<DiscoveredAsset> Items,
    IReadOnlyList<string> Warnings,
    string? ActingAs,
    IReadOnlyList<string>? UnavailableFields = null,
    IReadOnlyList<string>? SourceScope = null);
