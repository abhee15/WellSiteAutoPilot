namespace WellSiteAutoPilot.Application.Security;

/// <summary>Read-only proposed entitlement projection; never live authorization.</summary>
public sealed record ProposedAuthorizedAsset(Guid Id, string Name);

public interface IProposedAssetAccessRepository
{
    /// <summary>
    /// Authoritative SQL existence check for a single ACTIVE Asset and one
    /// resource permission. No cached principal scopes or cross-grant union.
    /// Still diagnostic only; does not replace live legacy access.
    /// </summary>
    Task<bool> IsAssetAuthorizedAsync(
        Guid userId, string permission, Guid assetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All requested ACTIVE Assets must each have this permission. An empty,
    /// duplicate or invalid Asset set fails closed, never authorizes a bulk
    /// mutation by vacuous success.
    /// </summary>
    Task<bool> AreAssetsAuthorizedAsync(
        Guid userId, string permission, IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deployment-wide permissions never imply access to individual Assets.
    /// Only a direct active deployment-wide grant can satisfy this query.
    /// </summary>
    Task<bool> IsDeploymentPermissionGrantedAsync(
        Guid userId, string permission,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProposedAuthorizedAsset>> ListAsync(
        Guid userId, string permission, int offset, int limit,
        CancellationToken cancellationToken = default);
}

