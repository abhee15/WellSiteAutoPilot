namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class LogicModulePackageEntity
{
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public DateTimeOffset UploadedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
}
