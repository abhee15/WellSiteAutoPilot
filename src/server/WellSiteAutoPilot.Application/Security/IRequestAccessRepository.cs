namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Fresh grant authority. Permission hints are coarse prerequisites and never
/// replace resource predicates or a mutation's transactional authority fence.
/// </summary>
public interface IRequestAccessRepository
{
    Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject,
        CancellationToken cancellationToken = default);

    Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission,
        CancellationToken cancellationToken = default);

    Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission,
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
}
