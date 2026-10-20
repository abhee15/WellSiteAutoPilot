namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class ConnectionEntity
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string AdapterKey { get; init; } = string.Empty;
    public int SettingsVersion { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public bool IsEnabled { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? LastTestOutcome { get; set; }
    public string? LastTestMessage { get; set; }
    public string? LastTestActingAs { get; set; }
    public DateTimeOffset? LastTestAtUtc { get; set; }
    public int? LastTestRevision { get; set; }
}
