using WellSiteAutoPilot.Domain.System;

namespace WellSiteAutoPilot.Application.System;

public interface IPlatformInformationService
{
    IReadOnlyCollection<PlatformComponent> GetComponents();
}
