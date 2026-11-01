namespace WellSiteAutoPilot.Persistence.Authoring;

/// <summary>One authoring draft; the serialized record carries the document, validation, build and test state.</summary>
public sealed class AuthoringDraftEntity
{
    public Guid Id { get; init; }
    public int Revision { get; set; }
    public string RecordJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    /// <summary>Bumped on every write so concurrent read-modify-write cycles never overwrite each other.</summary>
    public long Version { get; set; }
}

/// <summary>A built package pinned to its draft and build.</summary>
public sealed class AuthoringPackageEntity
{
    public Guid DraftId { get; init; }
    public Guid BuildId { get; init; }
    public byte[] Package { get; set; } = [];
}
