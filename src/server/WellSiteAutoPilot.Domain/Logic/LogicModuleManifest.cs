using System.Text.Json;

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
    IReadOnlyCollection<LogicCommandRequirement> CommandRequirements,
    // Optional, human-readable summary of what the module does (shown in the catalog).
    string? Description = null,
    // Optional JSON Schema (the same subset Asset Types use) describing the module's settings.
    JsonElement? ParameterSchema = null);
