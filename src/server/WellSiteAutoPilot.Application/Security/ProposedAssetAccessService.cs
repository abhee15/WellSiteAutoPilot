using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed record ProposedAssetAccessView(
    Guid UserId,
    string Permission,
    int Offset,
    int Limit,
    IReadOnlyCollection<ProposedAuthorizedAsset> Assets,
    bool IsDiagnosticOnly = true);

public sealed class ProposedAssetAccessService(
    AccessGrantAdministrationService grants,
    IUserAccessRepository users,
    IProposedAssetAccessRepository assets,
    IProposedAssetAccessAuditRepository audit,
    TimeProvider clock)
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
        var view = new ProposedAssetAccessView(userId, permission, offset, limit, result);
        await audit.RecordAsync(view, actor, clock.GetUtcNow(), cancellationToken);
        return view;
    }
}
