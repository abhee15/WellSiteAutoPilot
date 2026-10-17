using Microsoft.AspNetCore.Routing;
using System.Diagnostics;
using System.Text.Json;
using WellSiteAutoPilot.Http;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>Opt-in CI observations, never an authentication mechanism.</summary>
public sealed partial class WindowsAuthenticationDiagnosticsMiddleware(
    RequestDelegate next, ILogger<WindowsAuthenticationDiagnosticsMiddleware> logger)
{
    private static readonly string[] AuthenticationKinds = ["NTLM", "Kerberos", "Negotiate"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var started = Stopwatch.GetTimestamp();
            context.Response.OnStarting(() =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var observation = Observe(context, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    var json = JsonSerializer.Serialize(observation);
                    WriteObservation(logger, json);
                }
                return Task.CompletedTask;
            });
        }
        await next(context);
    }

    public static WindowsAuthenticationObservation Observe(HttpContext context, double elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        var reportedKind = context.User.Identity?.AuthenticationType;
        var kind = !authenticated ? "Anonymous" :
            AuthenticationKinds
                .FirstOrDefault(value => string.Equals(value, reportedKind, StringComparison.OrdinalIgnoreCase)) ?? "Other";
        // Capture only the presence of the scheme, never its token. No raw
        // headers, claim values, identities, paths, queries or bodies enter this DTO.
        var challenge = context.Response.StatusCode == StatusCodes.Status401Unauthorized &&
            context.Response.Headers.WWWAuthenticate.Any(value => value is not null &&
                value.StartsWith("Negotiate", StringComparison.OrdinalIgnoreCase));
        return new WindowsAuthenticationObservation(DateTimeOffset.UtcNow,
            context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "UnmatchedApi",
            context.Request.Protocol, context.Response.StatusCode, authenticated, kind, challenge,
            CorrelationContext.GetCorrelationId(context), context.Connection.Id,
            double.IsFinite(elapsedMilliseconds) ? Math.Max(0, elapsedMilliseconds) : 0);
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Information, Message = "WSA_WINDOWS_AUTH {Observation}")]
    private static partial void WriteObservation(ILogger logger, string observation);
}

public sealed record WindowsAuthenticationObservation(DateTimeOffset ObservedAtUtc, string Endpoint,
    string Protocol, int Status, bool Authenticated, string AuthenticationKind, bool NegotiateChallenge,
    string CorrelationId, string ConnectionId, double ElapsedMilliseconds);
