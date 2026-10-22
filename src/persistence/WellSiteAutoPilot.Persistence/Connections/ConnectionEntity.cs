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

    /// <summary>Who ran the last test: a person ("manual") or the connection monitor ("scheduled").</summary>
    public string? LastTestSource { get; set; }

    /// <summary>Scheduled checks that failed in a row. A person's test or one success resets it.</summary>
    public int ConsecutiveFailureCount { get; set; }

    /// <summary>
    /// When the monitor may next check this connection. Null means as soon as it looks. While a check is running this
    /// holds a short lease, so another Server instance does not check the same connection at the same time.
    /// </summary>
    public DateTimeOffset? NextCheckAtUtc { get; set; }
}
