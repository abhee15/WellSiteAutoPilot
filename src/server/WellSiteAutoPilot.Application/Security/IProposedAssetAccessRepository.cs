using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Read-only resource listing for the not-yet-active scoped entitlement model.
/// SQL must filter on an indivisible grant/role/Asset tuple before paging.
/// </summary>
public sealed record ProposedAuthorizedAsset(Guid Id, string Name);

public sealed record ProposedAssetAccessView(
    Guid UserId,
    string Permission,
    int Offset,
    int Limit,
    IReadOnlyCollection<ProposedAuthorizedAsset> Assets,
    bool IsDiagnosticOnly = true);

public interface IProposedAssetAccessRepository
{
    Task<IReadOnlyCollection<ProposedAuthorizedAsset>> ListAsync(
        Guid userId, string permission, int offset, int limit,
        CancellationToken cancellationToken = default);
}

public sealed class ProposedAssetAccessService(
    AccessGrantAdministrationService grants,
    IUserAccessRepository users,
    IProposedAssetAccessRepository assets)
{
    public async Task<ProposedAssetAccessView> ListAsync(
        Guid userId, string permission, int offset, int limit,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        await grants.RequireAdministratorAsync(actor, cancellationToken);
        if (userId == Guid.Empty || offset < 0 || offset > 1_000_000 ||
            limit is < 1 or > 500 ||
            !ProductPermissionCatalog.Definitions.Any(item =>
                item.Key == permission && item.ScopeKind == "ResourceScoped"))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_PREVIEW_QUERY_INVALID", FailureKind.Validation,
                "A valid user, resource permission and bounded page are required.");
        }

        var subject = await users.GetAsync(userId, cancellationToken);
        if (subject is null || !subject.IsActive)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE", FailureKind.NotFound,
                "The user must be active for a scoped access preview.");
        }

        var result = await assets.ListAsync(
            userId, permission, offset, limit, cancellationToken);
        return new ProposedAssetAccessView(userId, permission, offset, limit, result);
    }
}
