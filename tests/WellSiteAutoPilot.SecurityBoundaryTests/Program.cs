using System.Security.Claims;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Security;

var authorizer = new AssetScopeAuthorizer();
var wellA = Guid.NewGuid();
var wellB = Guid.NewGuid();
var operatorOnA = Principal(false, wellA.ToString());
var adminWildcard = Principal(true, "*");

var scoped = authorizer.GetAllowedAssetIds(operatorOnA);
if (scoped is null || !scoped.SequenceEqual([wellA]))
{
    throw new InvalidOperationException("Exact legacy Asset scope was not retained.");
}
authorizer.RequireAsset(operatorOnA, wellA);
authorizer.RequireAssets(operatorOnA, [wellA]);
authorizer.RequireAssets(operatorOnA, [wellA, wellA]);
ExpectCode(() => authorizer.RequireAsset(Principal(false), wellA),
    "SECURITY_ASSET_SCOPE_FORBIDDEN");
var explicitAdmin = Principal(true, wellA.ToString());
if (authorizer.GetAllowedAssetIds(explicitAdmin) is null)
    throw new InvalidOperationException("Admin role silently widened explicit scope.");
ExpectCode(() => authorizer.RequireAsset(operatorOnA, wellB),
    "SECURITY_ASSET_SCOPE_FORBIDDEN");

authorizer.RequireAsset(adminWildcard, wellA);
authorizer.RequireAssets(adminWildcard, [wellA, wellB]);
authorizer.RequireAdministrator(adminWildcard);
ExpectCode(() => authorizer.RequireAdministrator(operatorOnA),
    "SECURITY_ADMIN_REQUIRED");

// Never ignore a malformed Asset in a multi-Asset mutation. This check
// precedes wildcard handling, so even an administrator cannot approve it.
foreach (var principal in new[] { operatorOnA, adminWildcard })
{
    ExpectCode(() => authorizer.RequireAssets(principal, [wellA, Guid.Empty]),
        "SECURITY_ASSET_SCOPE_FORBIDDEN");
    ExpectCode(() => authorizer.RequireAssets(principal, [Guid.Empty]),
        "SECURITY_ASSET_SCOPE_FORBIDDEN");
    ExpectCode(() => authorizer.RequireAssets(principal, []),
        "SECURITY_ASSET_SCOPE_FORBIDDEN");
}

foreach (var malformedScope in new[]
{
    Principal(false, "not-a-guid"),
    Principal(false, Guid.Empty.ToString()),
    Principal(false, "*"),
    Principal(true, "*", wellA.ToString()),
    Principal(true, "*", "*"),
    Principal(false, "*", wellA.ToString())
})
{
    ExpectCode(() => authorizer.GetAllowedAssetIds(malformedScope),
        "SECURITY_ASSET_SCOPE_INVALID");
    ExpectCode(() => authorizer.RequireAsset(malformedScope, wellA),
        "SECURITY_ASSET_SCOPE_INVALID");
}

// An administrator with explicit scopes but no wildcard is not implicitly
// permitted to use all Assets: privilege and scope stay separate.
var limitedAdmin = Principal(true, wellA.ToString());
authorizer.RequireAsset(limitedAdmin, wellA);
ExpectCode(() => authorizer.RequireAsset(limitedAdmin, wellB),
    "SECURITY_ASSET_SCOPE_FORBIDDEN");

Console.WriteLine("Asset-scope authorization boundary checks passed.");
return 0;

static ClaimsPrincipal Principal(bool administrator, params string[] scopeClaims)
{
    var claims = scopeClaims
        .Select(scope => new Claim(SecurityClaimTypes.AssetScope, scope))
        .ToList();
    if (administrator)
    {
        claims.Add(new Claim(SecurityClaimTypes.Role, "Admin"));
    }

    return new ClaimsPrincipal(new ClaimsIdentity(claims, "SecurityBoundaryTests"));
}

static void ExpectCode(Action assertion, string expectedCode)
{
    try
    {
        assertion();
    }
    catch (WellSiteAutoPilotException error) when (
        error.Code == expectedCode &&
        error.Kind == FailureKind.Authorization)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Security boundary did not reject invalid input with {expectedCode}.");
}
