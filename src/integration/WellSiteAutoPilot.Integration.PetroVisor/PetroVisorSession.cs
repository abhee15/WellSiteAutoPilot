using MyrConn.PetroVisorFramework.API;

namespace WellSiteAutoPilot.Integration.PetroVisor;

/// <summary>
/// Builds a signed-in SDK session for one workspace. How the Gateway signs in lives behind this seam so the
/// choice (Windows single sign-on through an identity provider, or a service sign-in) never reaches the adapter.
/// </summary>
public interface IPetroVisorSessionFactory
{
    /// <summary>The identity PetroVisor will see, stated on connection tests. Never a secret.</summary>
    string Identity { get; }

    PetroVisorServiceProvider Create(Uri discoveryServiceUrl, string workspace, Uri? identityProvider = null);
}

/// <summary>The Gateway has no way to sign in to PetroVisor yet. The message is safe to show.</summary>
public sealed class PetroVisorSignInUnavailableException(string message) : Exception(message);

/// <summary>
/// STOPGAP for testing until the Windows single sign-on route is chosen: signs in with a user name and password
/// that the machine's administrator places in the Gateway service's environment. Nothing is stored by the
/// platform, and nothing about it is saved in a connection.
/// </summary>
public sealed class PetroVisorPasswordSessionFactory(Func<string, string?> environment) : IPetroVisorSessionFactory
{
    public const string UserVariable = "WSA_PETROVISOR_USER";
    public const string PasswordVariable = "WSA_PETROVISOR_PASSWORD";

    public string Identity => environment(UserVariable) is { Length: > 0 } user ? user : "(no PetroVisor sign-in configured)";

    public PetroVisorServiceProvider Create(Uri discoveryServiceUrl, string workspace, Uri? identityProvider = null)
    {
        ArgumentNullException.ThrowIfNull(discoveryServiceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        var user = environment(UserVariable);
        var password = environment(PasswordVariable);
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
        {
            throw new PetroVisorSignInUnavailableException(
                $"The Gateway has no PetroVisor sign-in yet. Set {UserVariable} and {PasswordVariable} for the Gateway service (testing only).");
        }

        return new PetroVisorServiceProvider(discoveryServiceUrl, workspace, user, password);
    }
}
