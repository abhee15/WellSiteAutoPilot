using System.Security.Claims;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Security;

public sealed class AssetScopeAuthorizer
{
    public IReadOnlyCollection<Guid>? GetAllowedAssetIds(
        ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var scopeValues = principal.FindAll(SecurityClaimTypes.AssetScope)
            .Select(item => item.Value)
            .ToArray();

        if (scopeValues.Contains("*", StringComparer.Ordinal))
        {
            return null;
        }

        var assetIds = new List<Guid>(scopeValues.Length);

        foreach (var value in scopeValues)
        {
            if (!Guid.TryParse(value, out var assetId))
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_ASSET_SCOPE_INVALID",
                    FailureKind.Authorization,
                    "The authenticated Asset scope contains an invalid identifier.");
            }

            assetIds.Add(assetId);
        }

        return assetIds.Distinct().OrderBy(item => item).ToArray();
    }

    public void RequireAsset(
        ClaimsPrincipal principal,
        Guid assetId) =>
        RequireAssets(principal, [assetId]);

    public void RequireAssets(
        ClaimsPrincipal principal,
        IEnumerable<Guid> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var allowed = GetAllowedAssetIds(principal);
        if (allowed is null)
        {
            return;
        }

        var allowedSet = allowed.ToHashSet();
        var requested = assetIds
            .Where(item => item != Guid.Empty)
            .Distinct()
            .ToArray();

        if (requested.Length == 0 ||
            requested.Any(item => !allowedSet.Contains(item)))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ASSET_SCOPE_FORBIDDEN",
                FailureKind.Authorization,
                "The current user is not authorized for one or more requested Assets.");
        }
    }

    public void RequireAdministrator(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!principal.HasClaim(
                SecurityClaimTypes.Role,
                "Admin"))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED",
                FailureKind.Authorization,
                "This operation requires the Admin role.");
        }
    }
}
