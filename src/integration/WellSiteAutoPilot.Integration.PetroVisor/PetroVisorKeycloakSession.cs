using System.Text.Json;
using MyrConn.PetroVisorFramework.API;

namespace WellSiteAutoPilot.Integration.PetroVisor;

/// <summary>
/// Signs the Gateway in to PetroVisor as a service account on the PetroVisor identity provider (Keycloak), using the
/// OAuth client-credentials grant. The identity provider decides how people sign in (its own accounts or federated
/// Windows); a service account needs none of that and works unattended. The client id and secret live in the Gateway
/// service's environment, never in a connection.
/// </summary>
public sealed class PetroVisorKeycloakSessionFactory : IPetroVisorSessionFactory, IDisposable
{
    public const string ClientIdVariable = "WSA_PETROVISOR_CLIENT_ID";
    public const string ClientSecretVariable = "WSA_PETROVISOR_CLIENT_SECRET";
    public const string RealmTokenPath = "/protocol/openid-connect/token";

    private readonly Func<string, string?> _environment;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public PetroVisorKeycloakSessionFactory(Func<string, string?> environment, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _ownsHttp = true;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public static bool IsConfigured(Func<string, string?> environment) =>
        !string.IsNullOrWhiteSpace(environment(ClientIdVariable)) && !string.IsNullOrEmpty(environment(ClientSecretVariable));

    public string Identity =>
        _environment(ClientIdVariable) is { Length: > 0 } id ? $"service account {id}" : "(no PetroVisor sign-in configured)";

    /// <summary>
    /// The address that issues tokens. An address ending in /token is used as given; anything else is taken as the
    /// Keycloak realm address. With none given, null: the PetroVisor discovery service is asked instead.
    /// </summary>
    public static Uri? TokenEndpoint(Uri? identityProvider)
    {
        if (identityProvider is null)
        {
            return null;
        }

        var text = identityProvider.AbsoluteUri.TrimEnd('/');
        return text.EndsWith("/token", StringComparison.Ordinal) ? new Uri(text) : new Uri(text + RealmTokenPath);
    }

    public PetroVisorServiceProvider Create(Uri discoveryServiceUrl, string workspace, Uri? identityProvider = null)
    {
        ArgumentNullException.ThrowIfNull(discoveryServiceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        var clientId = _environment(ClientIdVariable);
        var secret = _environment(ClientSecretVariable);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrEmpty(secret))
        {
            throw new PetroVisorSignInUnavailableException(
                $"The Gateway has no PetroVisor sign-in yet. Set {ClientIdVariable} and {ClientSecretVariable} for the Gateway service.");
        }

        var endpoint = TokenEndpoint(identityProvider) ?? Discover(discoveryServiceUrl, workspace);
        var token = RequestAccessToken(endpoint, clientId, secret);
        return new PetroVisorServiceProvider(
            discoveryServiceUrl, workspace, new NativeClientTokens { AccessToken = token, RefreshToken = string.Empty });
    }

    private static Uri Discover(Uri discoveryServiceUrl, string workspace)
    {
        try
        {
            var configuration = new ClientConfiguration(discoveryServiceUrl, workspace, new NativeClientTokens());
            configuration.DiscoverConfiguration();
            if (!string.IsNullOrWhiteSpace(configuration.TokenEndPoint) &&
                Uri.TryCreate(configuration.TokenEndPoint, UriKind.Absolute, out var found))
            {
                return found;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Fall through to the message below; the cause can include addresses and is not shown.
        }

        throw new PetroVisorSignInUnavailableException(
            "PetroVisor did not say where to sign in. Enter the identity provider address on the connection.");
    }

    /// <summary>Asks the identity provider for an access token as the service account. Errors are safe to show.</summary>
    public string RequestAccessToken(Uri endpoint, string clientId, string secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = secret
            })
        };

        try
        {
            using var response = _http.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new PetroVisorSignInUnavailableException(
                    response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized
                        ? "The identity provider did not accept the Gateway's PetroVisor client id and secret."
                        : $"The identity provider answered {(int)response.StatusCode}.");
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("access_token", out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } token)
            {
                return token;
            }

            throw new PetroVisorSignInUnavailableException("The identity provider answered without an access token.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new PetroVisorSignInUnavailableException("The identity provider could not be reached or gave an unreadable answer.");
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
