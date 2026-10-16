using System.Security.Claims;
using System.Globalization;
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

        if (authenticatedPrincipal.Identity is not ClaimsIdentity identity ||
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

        // Read only the primary authenticated identity, not arbitrary
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
        if (sidClaims.Length == 1 && !IsValidSid(sid))
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

    // MS-DTYP 2.4.2.1/2.4.2.2: canonical revision-1 SID string,
    // 48-bit authority (decimal below 2^32, otherwise 12 hex digits),
    // and 1..15 unsigned 32-bit subauthorities. No platform-specific API
    // is needed to validate the trusted host's serialized identity on Linux.
    private static bool IsValidSid(string? sid)
    {
        if (sid is null || sid.Length > 252) return false;
        var parts = sid.Split('-');
        if (parts.Length is < 4 or > 18 ||
            !string.Equals(parts[0], "S", StringComparison.OrdinalIgnoreCase) ||
            parts[1] != "1") return false;
        var authority = parts[2];
        if (authority.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (authority.Length != 14 ||
                !ulong.TryParse(authority.AsSpan(2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var value) || value < 0x100000000)
                return false;
        }
        else if (!IsDecimalUInt32(authority)) return false;
        return parts.Skip(3).All(IsDecimalUInt32);
    }

    private static bool IsDecimalUInt32(string value) =>
        value.Length is >= 1 and <= 10 &&
        (value.Length == 1 || value[0] != '0') &&
        value.All(char.IsAsciiDigit) &&
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static WellSiteAutoPilotException Denied(string code, string detail) =>
        new(code, FailureKind.Authorization, detail);
}
