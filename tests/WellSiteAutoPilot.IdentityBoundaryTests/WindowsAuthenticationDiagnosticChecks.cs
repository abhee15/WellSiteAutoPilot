using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using WellSiteAutoPilot.Server.Security;

internal static class WindowsAuthenticationDiagnosticChecks
{
    public static void Run()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "PRIVATE_IDENTITY")], "PRIVATE_PROVIDER")),
            TraceIdentifier = "opaque-correlation"
        };
        context.Connection.Id = "opaque-connection";
        context.Request.Protocol = "HTTP/1.1";
        context.Request.Path = "/api/v1/PRIVATE_PATH";
        context.Request.QueryString = new QueryString("?secret=PRIVATE_QUERY");
        context.Request.Headers.Authorization = "Negotiate PRIVATE_REQUEST_TOKEN";
        context.Response.Headers.WWWAuthenticate = "Negotiate PRIVATE_RESPONSE_TOKEN";
        context.Response.StatusCode = 401;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointNameMetadata("GetCurrentUser")), "PRIVATE_DISPLAY_NAME"));
        var observation = WindowsAuthenticationDiagnosticsMiddleware.Observe(context, 3);
        var json = JsonSerializer.Serialize(observation);
        if (json.Contains("PRIVATE_", StringComparison.Ordinal) || !observation.NegotiateChallenge ||
            observation.Endpoint != "GetCurrentUser" || observation.AuthenticationKind != "Other" ||
            observation.ConnectionId != "opaque-connection" || observation.CorrelationId != "opaque-correlation")
            throw new InvalidOperationException("Windows diagnostics leaked raw authentication or request data.");
        context.User = new ClaimsPrincipal();
        context.Response.StatusCode = 200;
        var anonymous = WindowsAuthenticationDiagnosticsMiddleware.Observe(context, double.NaN);
        if (anonymous.Authenticated || anonymous.AuthenticationKind != "Anonymous" || anonymous.NegotiateChallenge ||
            anonymous.ElapsedMilliseconds != 0)
            throw new InvalidOperationException("Diagnostic observations misreported identity, challenge or elapsed time.");
        Console.WriteLine("Windows diagnostic observations omit identities, token/header values, paths and queries.");
    }
}
