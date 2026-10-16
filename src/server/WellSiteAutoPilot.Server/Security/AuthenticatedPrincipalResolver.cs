using System.Security.Claims;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// The provider-verified external identity is distinct from the internal WSA
/// user GUID and from application roles/Asset permissions.
/// PersistedIdentityKey intentionally preserves V1 SID:/NAME: keys so existing
/// Windows accounts are not silently re-created or reassigned permissions.
/// </summary>
public sealed record AuthenticatedExternalIdentity(
    string ProviderId,
    string SubjectId,
    string PersistedIdentityKey,
    string IdentityName,
    string? DisplayName);

public interface IAuthenticatedPrincipalResolver
{
    AuthenticatedExternalIdentity Resolve(ClaimsPrincipal authenticatedPrincipal);
}

/// <summary>
/// Host-boundary adapter for currently supported authentication modes.
/// The ASP.NET Core authentication middleware MUST have authenticated the
/// principal using the host's configured scheme before this resolver is used.
/// This is not an identity provider and never interprets product permissions.
/// A future provider adds its own trusted adapter and explicit identity-link
/// migration; it must not borrow Windows SID:/NAME: keys by coincidence.
/// </summary>
public sealed class ConfiguredPrincipalResolver(string authenticationMode)
    : IAuthenticatedPrincipalResolver
{
    public AuthenticatedExternalIdentity Resolve(ClaimsPrincipal authenticatedPrincipal)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);

        if (authenticatedPrincipal.Identities.FirstOrDefault() is not { } identity ||
            !identity.IsAuthenticated)
        {
            throw Denied("SECURITY_IDENTITY_REQUIRED",
                "An authenticated identity is required.");
        }

        // Match the host's existing case-insensitive authentication-mode
        // selection. A capitalization-only configuration change must not
        // authenticate successfully but fail during internal user resolution.
        var provider = string.Equals(authenticationMode,
            SecurityOptions.NegotiateAuthenticationMode,
            StringComparison.OrdinalIgnoreCase)
            ? "windows"
            : string.Equals(authenticationMode,
                SecurityOptions.TestHeaderAuthenticationMode,
                StringComparison.OrdinalIgnoreCase)
                ? "ci-test-header"
                : throw Denied("SECURITY_IDENTITY_PROVIDER_UNSUPPORTED",
                    "The configured identity provider is unsupported.");

        // Read only the first host-supplied authenticated identity, not arbitrary
        // secondary identities that could carry misleading SID/name claims.
        if (identity.FindAll(identity.NameClaimType).Skip(1).Any())
        {
            throw Denied("SECURITY_IDENTITY_AMBIGUOUS",
                "The authenticated identity has ambiguous account name claims.");
        }
        var normalizedName = UserAccessService.NormalizeIdentity(
            identity.Name ?? string.Empty);
        var sidClaims = identity.FindAll(ClaimTypes.PrimarySid).ToArray();
        if (sidClaims.Length > 1)
        {
            throw Denied("SECURITY_IDENTITY_SID_AMBIGUOUS",
                "The authenticated identity has ambiguous primary SID claims.");
        }

        var sid = sidClaims.SingleOrDefault()?.Value?.Trim();
        if (sidClaims.Length == 1 && !WindowsSidFormat.IsValid(sid))
        {
            throw Denied("SECURITY_IDENTITY_KEY_INVALID",
                "The authenticated primary SID is invalid.");
        }

        var legacyKey = sid is not null
            ? $"SID:{sid}"
            : $"NAME:{normalizedName}";

        // This normalization is identical to the existing persisted user key
        // normalization and therefore preserves SID-/name-based account links.
        var persistedKey = UserAccessService.NormalizeIdentityKey(legacyKey);
        var displayName = identity.FindFirst("name")?.Value;
        if (displayName is null && provider == "ci-test-header")
        {
            displayName = identity.FindFirst("wsa:test-display-name")?.Value;
        }

        return new AuthenticatedExternalIdentity(
            provider,
            sid is not null ? persistedKey["SID:".Length..] : normalizedName,
            persistedKey,
            identity.Name!.Trim(),
            displayName);
    }

    private static WellSiteAutoPilotException Denied(string code, string detail) =>
        new(code, FailureKind.Authorization, detail);
}
