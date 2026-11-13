namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class GuidedModuleSubmissionEntity
{
    public Guid DraftId { get; set; }
    public int Revision { get; set; }
    public string PackageSha256 { get; set; } = string.Empty;
    public byte[] PackageBytes { get; set; } = [];
    public string EvidenceJson { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
