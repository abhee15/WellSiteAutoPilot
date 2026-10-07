namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicRevisionEntity
{
    public Guid Id { get; init; }
    public Guid ConfiguredLogicId { get; init; }
    public int RevisionNumber { get; init; }
    public string ModuleId { get; set; } = string.Empty;
    public string ModuleVersion { get; set; } = string.Empty;
    public string ModuleManifestJson { get; set; } = "{}";
    public string Mode { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public string? ScheduleJson { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ValidatedAtUtc { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
}
