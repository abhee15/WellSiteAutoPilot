namespace WellSiteAutoPilot.Domain.Logic;

public sealed record LogicModuleManifest(
    int ManifestVersion,
    string ModuleId,
    string Version,
    string DisplayName,
    string Publisher,
    LogicRuntimeKind Runtime,
    ExecutionProfile ExecutionProfile,
    string EntryPoint,
    string MinimumSdkVersion,
    IReadOnlyCollection<LogicAssetRequirement> AssetRequirements,
    IReadOnlyCollection<LogicDataRequirement> DataRequirements,
    IReadOnlyCollection<LogicCommandRequirement> CommandRequirements);
