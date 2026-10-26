using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Logic;

public interface ILogicModulePackageRepository
{
    Task<InstalledLogicModule> UploadAuthorizedAsync(InstalledLogicModule logicModule, byte[] archive,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);
}
