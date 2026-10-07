namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class LogicModuleCatalogEntity
{
    public Guid Id { get; init; }
    public string ModuleId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string ExecutionProfile { get; init; } = string.Empty;
    public string ManifestJson { get; init; } = "{}";
    public string PackageSha256 { get; init; } = string.Empty;
    public string TrustStatus { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; }
}
