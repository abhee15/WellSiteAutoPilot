namespace WellSiteAutoPilot.Domain.Logic;

public sealed record InstalledLogicModule(
    Guid Id,
    string ModuleId,
    string Version,
    string DisplayName,
    string Publisher,
    LogicRuntimeKind Runtime,
    ExecutionProfile ExecutionProfile,
    string ManifestJson,
    string PackageSha256,
    LogicModuleTrustStatus TrustStatus,
    bool IsEnabled,
    DateTimeOffset InstalledAtUtc);
