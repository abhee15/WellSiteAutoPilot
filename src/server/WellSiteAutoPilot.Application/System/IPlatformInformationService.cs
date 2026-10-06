using WellSiteAutoPilot.Domain.System;

namespace WellSiteAutoPilot.Application.System;

public interface IPlatformInformationService
{
    Task<IReadOnlyCollection<PlatformComponent>> GetComponentsAsync(
        CancellationToken cancellationToken = default);
}
