namespace WellSiteAutoPilot.Domain.Logic;

public sealed record LogicAssetRequirement(
    string Role,
    int MinimumCount,
    int MaximumCount,
    string? RequiredAssetTypeKey,
    IReadOnlyCollection<string> RequiredTraits);
