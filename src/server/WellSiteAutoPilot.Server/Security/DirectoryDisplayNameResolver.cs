using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.Extensions.Caching.Memory;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// Finds a person's friendly name (for example "Alex Rivera") for an already authenticated user.
/// A Windows sign-in only carries the login (DOMAIN\account), which is often an employee number.
/// The result is presentation metadata only: it never affects identity, grants or authorization,
/// and a failed lookup must never block a sign-in.
/// </summary>
public interface IDirectoryDisplayNameResolver
{
    Task<string?> ResolveAsync(ClaimsPrincipal principal, AuthenticatedExternalIdentity externalIdentity,
        CancellationToken cancellationToken);
}

/// <summary>
/// Looks the authenticated Windows account up by SID in the domain directory, then in the
/// machine's local accounts. Lookups are cached and time-boxed because a domain controller
/// can be slow or unreachable (for example a VPN that is not connected).
/// </summary>
public sealed class WindowsDirectoryDisplayNameResolver(IMemoryCache cache) : IDirectoryDisplayNameResolver
{
    private const int MaximumLength = 200;
    private static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);

    public async Task<string?> ResolveAsync(ClaimsPrincipal principal, AuthenticatedExternalIdentity externalIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(externalIdentity);
        if (externalIdentity.ProviderId != "windows" || !OperatingSystem.IsWindows() ||
            principal.Identities.FirstOrDefault() is not WindowsIdentity identity ||
            identity.User?.Value is not { } sid ||
            !string.Equals(externalIdentity.PersistedIdentityKey, $"SID:{sid}", StringComparison.OrdinalIgnoreCase))
            return null;

        var cacheKey = $"directory-display-name:{sid}";
        if (cache.TryGetValue(cacheKey, out string? cached)) return cached;

        var name = await LookUpAsync(sid, cancellationToken);
        cache.Set(cacheKey, name, name is null ? MissingLifetime : FoundLifetime);
        return name;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string?> LookUpAsync(string sid, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => Find(sid), CancellationToken.None).WaitAsync(LookupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? Find(string sid)
    {
        foreach (var contextType in new[] { ContextType.Domain, ContextType.Machine })
        {
            try
            {
                using var context = new PrincipalContext(contextType);
                using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
                var name = Clean(user?.DisplayName) ?? Clean($"{user?.GivenName} {user?.Surname}");
                if (name is not null) return name;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // No reachable domain (offline VPN, workgroup machine) or an unreadable record:
                // fall through to the next source. The caller shows the login instead.
            }
        }

        return null;
    }

    internal static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (text.Length > MaximumLength) text = text[..MaximumLength].TrimEnd();
        return text.Length == 0 ? null : text;
    }
}

/// <summary>Used where the provider supplies no directory (TestHeader, non-Windows hosts).</summary>
public sealed class NoDirectoryDisplayNameResolver : IDirectoryDisplayNameResolver
{
    public Task<string?> ResolveAsync(ClaimsPrincipal principal, AuthenticatedExternalIdentity externalIdentity,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
