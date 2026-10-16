using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class RequestAccessRepository(WellSiteAutoPilotDbContext db, TimeProvider clock)
    : IRequestAccessRepository
{
    public async Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return await ScopedAccessQueries.Permissions(db, subject.UserId, subject.WindowsGroups,
            clock.GetUtcNow()).OrderBy(permission => permission).ToArrayAsync(cancellationToken);
    }

    public Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return ScopedAccessQueries.DeploymentGrants(db, subject.UserId, permission,
            subject.WindowsGroups, clock.GetUtcNow()).AnyAsync(cancellationToken);
    }

    public async Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission,
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count == 0 || assetIds.Contains(Guid.Empty)) return false;
        var targets = assetIds.Distinct().ToArray();
        return await ScopedAccessQueries.Assets(db, subject.UserId, permission,
            subject.WindowsGroups, clock.GetUtcNow()).CountAsync(asset => targets.Contains(asset.Id),
                cancellationToken) == targets.Length;
    }
}
