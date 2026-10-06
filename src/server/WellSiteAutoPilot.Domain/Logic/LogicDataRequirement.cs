namespace WellSiteAutoPilot.Domain.Logic;

public sealed record LogicDataRequirement(
    string Id,
    string AssetRole,
    string Quantity,
    string Access,
    string? CanonicalUnit,
    int? MaximumAgeSeconds,
    bool AllowUncertainQuality);
