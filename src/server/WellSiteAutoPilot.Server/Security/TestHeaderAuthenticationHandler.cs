using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WellSiteAutoPilot.Server.Security;

public sealed class TestHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    public const string SchemeName = "WellSiteAutoPilot.TestHeader";
    public const string IdentityHeader = "X-WSA-Test-Identity";
    public const string DisplayNameHeader = "X-WSA-Test-Display-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(
                IdentityHeader,
                out var identityValues) ||
            string.IsNullOrWhiteSpace(identityValues.ToString()))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    $"Missing {IdentityHeader} header."));
        }

        var identityName = identityValues.ToString().Trim();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, identityName),
            new(ClaimTypes.NameIdentifier, identityName)
        };

        if (Request.Headers.TryGetValue(
                DisplayNameHeader,
                out var displayNameValues) &&
            !string.IsNullOrWhiteSpace(displayNameValues.ToString()))
        {
            claims.Add(
                new Claim(
                    "wsa:test-display-name",
                    displayNameValues.ToString().Trim()));
        }

        var identity = new ClaimsIdentity(
            claims,
            SchemeName,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
