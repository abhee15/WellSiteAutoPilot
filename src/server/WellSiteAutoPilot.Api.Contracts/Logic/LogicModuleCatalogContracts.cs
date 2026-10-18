namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record RegisterLogicModuleRequest(
    string ManifestJson,
    string PackageSha256);

public sealed record ChangeLogicModuleApprovalRequest(
    string Reason);

public sealed record LogicModuleResponse(
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
