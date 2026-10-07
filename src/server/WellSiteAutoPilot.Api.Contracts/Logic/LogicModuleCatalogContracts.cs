namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record RegisterLogicModuleRequest(
    string ManifestJson,
    string PackageSha256);

public sealed record LogicModuleResponse(
    Guid Id,
    string ModuleId,
    string Version,
    string DisplayName,
    string Publisher,
    string Runtime,
    string ExecutionProfile,
    string ManifestJson,
    string PackageSha256,
    string TrustStatus,
    bool IsEnabled,
    DateTimeOffset InstalledAtUtc);
