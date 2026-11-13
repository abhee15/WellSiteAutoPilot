namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class GuidedModuleDraftEntity
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public int Revision { get; set; }
    public string ContentJson { get; set; } = string.Empty;
}
