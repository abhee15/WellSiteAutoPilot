namespace WellSiteAutoPilot.Domain.Security;

public sealed record UserAccessProfile(
    Guid Id,
    string IdentityName,
    string NormalizedIdentityName,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyCollection<ApplicationRole> Roles,
    IReadOnlyCollection<Guid> AssetScopeIds)
{
    public bool IsAdministrator => Roles.Contains(ApplicationRole.Admin);

    public bool HasAssetAccess(Guid assetId) =>
        IsAdministrator || AssetScopeIds.Contains(assetId);
}
