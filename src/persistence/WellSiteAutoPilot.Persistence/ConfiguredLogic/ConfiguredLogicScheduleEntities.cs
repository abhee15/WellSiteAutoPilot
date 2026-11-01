namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicScheduleStateEntity
{
    public Guid ConfiguredLogicId { get; init; }
    public Guid BasedOnRevisionId { get; set; }
    public int Version { get; set; }
    public bool Enabled { get; set; }
    public int CadenceSeconds { get; set; }
    public DateTimeOffset StartAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
}

public sealed class ConfiguredLogicScheduleChangeEntity
{
    public Guid Id { get; init; }
    public Guid ConfiguredLogicId { get; init; }
    public Guid BasedOnRevisionId { get; init; }
    public int Version { get; init; }
    public string Kind { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int CadenceSeconds { get; init; }
    public DateTimeOffset StartAtUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public string ChangedBy { get; init; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; init; }
    public string? Reason { get; init; }
}
