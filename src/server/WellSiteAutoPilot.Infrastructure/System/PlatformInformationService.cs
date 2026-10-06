using System.Reflection;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Domain.System;

namespace WellSiteAutoPilot.Infrastructure.System;

public sealed class PlatformInformationService : IPlatformInformationService
{
    public IReadOnlyCollection<PlatformComponent> GetComponents()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        return
        [
            new PlatformComponent("WellSite AutoPilot Server", version, PlatformComponentStatus.Healthy)
        ];
    }
}
