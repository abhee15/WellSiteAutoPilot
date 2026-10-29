using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WellSiteAutoPilot.Server.Security;

internal static class SecurityActorFactoryChecks
{
    public static void Run()
    {
        var userId = Guid.NewGuid();
        foreach (var mode in new[] { SecurityOptions.NegotiateAuthenticationMode, SecurityOptions.TestHeaderAuthenticationMode })
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.Name, "FIELD\\admin"),
                    new Claim(ClaimTypes.PrimarySid, "S-1-5-21-1-2-3-4"),
                    new Claim(ClaimTypes.GroupSid, "S-1-5-21-1-2-3-5"),
                    new Claim("wsa:group", "S-1-5-21-1-2-3-6")
                ], "test-provider"))
            };
            var factory = new SecurityActorFactory(new ConfiguredPrincipalResolver(mode),
                new WindowsTokenGroupResolver(mode, TimeProvider.System));
            var actor = factory.Create(context, userId);
            if (actor.UserId != userId || actor.IdentityName != "FIELD\\admin" || actor.Groups is not null ||
                actor.RequiresGroupProof != (mode == SecurityOptions.NegotiateAuthenticationMode))
                throw new InvalidOperationException("Administration actor confused provider identity and group claim authority.");
        }
        Console.WriteLine("Administration actor factory requires real Windows proof and rejects group claims/test-header authority.");
    }
}
