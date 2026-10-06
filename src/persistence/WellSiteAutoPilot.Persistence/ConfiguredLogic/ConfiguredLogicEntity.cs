namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicEntity
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid? ActiveRevisionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
