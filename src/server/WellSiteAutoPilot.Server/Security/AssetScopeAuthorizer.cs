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

        // In the legacy model only an active administrator receives a
        // universal Asset claim. A mixed wildcard/explicit scope is not a
        // valid principal and must never silently become unrestricted.
        if (scopeValues.Contains("*", StringComparer.Ordinal))
        {
            if (scopeValues.Length != 1 ||
                !principal.HasClaim(SecurityClaimTypes.Role, "Admin"))
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_ASSET_SCOPE_INVALID",
                    FailureKind.Authorization,
                    "The authenticated Asset scope contains an invalid wildcard.");
            }

            return null;
        }

        var assetIds = new List<Guid>(scopeValues.Length);

        foreach (var value in scopeValues)
        {
            if (!Guid.TryParse(value, out var assetId) ||
                assetId == Guid.Empty)
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

        // Validate the ENTIRE requested set before considering an Admin
        // wildcard. Filtering out Guid.Empty here could authorize a mutation
        // whose input includes an invalid Asset alongside a valid one.
        var requested = assetIds.ToArray();
        if (requested.Length == 0 ||
            requested.Any(item => item == Guid.Empty))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ASSET_SCOPE_FORBIDDEN",
                FailureKind.Authorization,
                "The current user is not authorized for one or more requested Assets.");
        }

        var allowed = GetAllowedAssetIds(principal);
        if (allowed is null)
        {
            return;
        }

        var allowedSet = allowed.ToHashSet();
        if (requested.Any(item => !allowedSet.Contains(item)))
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
