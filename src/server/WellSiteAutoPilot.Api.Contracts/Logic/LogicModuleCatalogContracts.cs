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

public sealed record LogicModulePackageStateResponse(bool IsStored, bool PublicationRecorded, bool ObjectAvailable,
    bool PublicationPending, int AttemptCount, string? LastFailureCode, DateTimeOffset? NextAttemptAtUtc,
    bool RuntimeCompatible, bool WorkerHealthy, bool GatewayHealthy, bool PreflightReady,
    string? CompatibilityFailureCode, DateTimeOffset ObservedAtUtc);
