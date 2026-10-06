namespace WellSiteAutoPilot.Domain.Logic;

public sealed record LogicCommandRequirement(
    string Id,
    string AssetRole,
    string Command,
    string? Quantity,
    string? CanonicalUnit);
