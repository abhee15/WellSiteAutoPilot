using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WellSiteAutoPilot.Http.ServiceCallers;

public sealed partial class ServiceCallerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ServiceCallerOptions> callers) : AuthenticationHandler<AuthenticationSchemeOptions>(
        schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header) || header.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());
        var value = header[0];
        var prefix = ServiceCaller.Scheme + " ";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var credential = value[prefix.Length..].Trim();
        var separator = credential.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
            return Task.FromResult(AuthenticateResult.Fail("Malformed service credential."));
        var name = credential[..separator];
        var key = credential[(separator + 1)..];

        // The same failure for an unknown caller and a wrong key, and the key is never logged.
        if (!callers.Value.Verifiers.TryGetValue(name, out var verifier) || !ServiceCallerKeys.Matches(key, verifier))
        {
            LogRejected(Logger, name.Length <= 32 ? name : name[..32]);
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credential."));
        }

        var identity = new ClaimsIdentity(
            new Claim[] { new(ServiceCaller.ClaimType, name), new(ClaimTypes.Name, "service:" + name) },
            ServiceCaller.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ServiceCaller.Scheme)));
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Rejected an internal call presenting an invalid credential for caller '{Caller}'.")]
    private static partial void LogRejected(ILogger logger, string caller);
}
