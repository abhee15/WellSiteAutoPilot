using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public interface ILogicModuleResolver
{
    ILogicModuleV1 Resolve(
        string moduleId,
        string version);
}
