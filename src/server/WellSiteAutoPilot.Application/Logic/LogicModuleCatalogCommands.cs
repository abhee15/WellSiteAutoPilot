using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Logic;

public sealed record RegisterLogicModuleCommand(
    string ManifestJson,
    string PackageSha256,
    LogicModuleTrustStatus TrustStatus);
