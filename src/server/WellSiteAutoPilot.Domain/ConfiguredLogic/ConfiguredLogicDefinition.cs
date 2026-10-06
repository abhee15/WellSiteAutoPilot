namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicDefinition(
    Guid Id,
    string Name,
    Guid? ActiveRevisionId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<ConfiguredLogicRevision> Revisions);
