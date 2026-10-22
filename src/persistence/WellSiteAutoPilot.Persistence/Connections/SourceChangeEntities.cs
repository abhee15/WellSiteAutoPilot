namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class SourceCheckEntity
{
    public Guid Id { get; init; }
    public Guid ConnectionId { get; init; }
    public int Revision { get; init; }
    public int ConnectionRevision { get; init; }
    public int? HierarchyRevision { get; init; }
    public string AcceptedBindingsFingerprint { get; init; } = "";
    public string Status { get; set; } = "Checking";
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid ActorUserId { get; init; }
    public string? ActingAs { get; set; }
    public string? SourceScopeJson { get; set; }
    public int Found { get; set; }
    public int Changed { get; set; }
    public int New { get; set; }
    public int NotReturned { get; set; }
    public int ComparisonUnavailable { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public string? UnavailableFieldsJson { get; set; }
}

/// <summary>Append-only diagnostic evidence. Checks, reversion and import never resolve it.</summary>
public sealed class SourceChangeEntity
{
    public Guid Id { get; init; }
    public Guid ConnectionId { get; init; }
    public Guid? AssetId { get; init; }
    public string ResourceKind { get; init; } = "";
    public string ExternalId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string Status { get; init; } = "";
    public string EvidenceJson { get; init; } = "{}";
    public string? AcceptedBaselineJson { get; init; }
    public string? ObservedFactsJson { get; init; }
    public Guid FirstCheckId { get; init; }
    public Guid LastCheckId { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class SourceChangeImpactEntity
{
    public Guid ChangeId { get; init; }
    public Guid RevisionId { get; init; }
    public string Kind { get; init; } = "";
    public string Reference { get; init; } = "";
    public DateTimeOffset FirstSeenAtUtc { get; init; }
}
