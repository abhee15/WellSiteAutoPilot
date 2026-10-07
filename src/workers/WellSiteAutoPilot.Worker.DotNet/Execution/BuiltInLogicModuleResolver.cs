using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class BuiltInLogicModuleResolver : ILogicModuleResolver
{
    private readonly IReadOnlyDictionary<string, ILogicModuleV1> _modules =
        new ILogicModuleV1[]
        {
            new SampleShadowReadModule()
        }.ToDictionary(
            item => Key(item.Identity.ModuleId, item.Identity.Version),
            StringComparer.OrdinalIgnoreCase);

    public ILogicModuleV1 Resolve(
        string moduleId,
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (_modules.TryGetValue(Key(moduleId, version), out var logicModule))
        {
            return logicModule;
        }

        throw new WellSiteAutoPilotException(
            "MODULE_NOT_AVAILABLE",
            FailureKind.DependencyUnavailable,
            $"Logic Module '{moduleId}' version '{version}' is not available in this .NET worker.");
    }

    private static string Key(string moduleId, string version) =>
        $"{moduleId.Trim()}@{version.Trim()}";
}
