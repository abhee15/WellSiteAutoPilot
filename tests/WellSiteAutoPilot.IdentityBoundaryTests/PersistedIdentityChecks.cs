using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;
using WellSiteAutoPilot.Server.Security;

internal static class PersistedIdentityChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(connection).Options;
        await using var db = new WellSiteAutoPilotDbContext(options);
        var repository = new UserAccessRepository(db);
        var service = new UserIdentityService(repository, TimeProvider.System);
        var name = "IDENTITY\\" + Guid.NewGuid().ToString("N");
        var stableSid = "S-1-5-21-900-901-902-" + Random.Shared.Next(1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var resolver = new ConfiguredPrincipalResolver(SecurityOptions.NegotiateAuthenticationMode);
        var groups = new WindowsTokenGroupResolver(SecurityOptions.NegotiateAuthenticationMode, TimeProvider.System);
        var nameProfile = await service.ResolveAuthenticatedAsync("NAME:" + name.ToUpperInvariant(), name, null);
        var sidProfile = await service.ResolveAuthenticatedAsync("SID:" + stableSid, name + "-sid", null);
        foreach (var id in new[] { nameProfile.Id, sidProfile.Id })
            db.UserRoles.Add(new UserRoleEntity { UserId = id, Role = "Customer Technician" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var (_, nameAccess, nameReached) = await InvokeAsync(Principal(name));
        if (!nameReached || nameAccess.Subject.UserId != nameProfile.Id) throw new InvalidOperationException("Name identity changed its persisted account.");
        var (_, sidAccess, sidReached) = await InvokeAsync(Principal(name + "-renamed", stableSid));
        if (!sidReached || sidAccess.Subject.UserId != sidProfile.Id ||
            (await repository.GetIdentityAsync(sidProfile.Id))?.IdentityName != name + "-renamed")
            throw new InvalidOperationException("SID rename changed its persisted account.");
        var (_, appeared, appearedReached) = await InvokeAsync(Principal(name, stableSid + "-2"));
        var (_, changed, changedReached) = await InvokeAsync(Principal(name + "-renamed", stableSid + "-1"));
        var (_, repeated, _) = await InvokeAsync(Principal(name, stableSid + "-2"));
        var freshIds = new[] { appeared.Subject.UserId, changed.Subject.UserId };
        if (!appearedReached || !changedReached || freshIds.Distinct().Count() != 2 ||
            freshIds.Contains(nameProfile.Id) || freshIds.Contains(sidProfile.Id) ||
            repeated.Subject.UserId != appeared.Subject.UserId ||
            await db.UserRoles.AnyAsync(item => freshIds.Contains(item.UserId)) ||
            await db.AccessGrants.AnyAsync(item => item.UserId != null && freshIds.Contains(item.UserId.Value)) ||
            await db.UserRoles.CountAsync(item => (item.UserId == nameProfile.Id || item.UserId == sidProfile.Id) && item.Role == "Customer Technician") != 2)
            throw new InvalidOperationException("SID changes inherited access, linked an account or changed legacy metadata.");
        foreach (var id in freshIds)
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(item => item.Action == "security.user.provisioned" && item.TargetId == id.ToString());
            if (audit.ActorUserId != id || !audit.ActorIdentity.StartsWith("IDENTITY\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Provisioning lost its persisted audit subject.");
        }
        await db.Users.Where(item => item.Id == sidProfile.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        var (inactive, inactiveAccess, reached) = await InvokeAsync(Principal(name + "-renamed", stableSid));
        if (reached || inactive.Response.StatusCode != StatusCodes.Status403Forbidden || inactiveAccess.TryGetSubject(inactive.User, out _))
            throw new InvalidOperationException("Disabled identity reached the grant runtime.");
        var counts = (await db.Users.CountAsync(), await db.AuditEvents.CountAsync());
        try { await InvokeAsync(Principal(name, "not-a-sid")); throw new InvalidOperationException("Malformed identity reached persistence."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "SECURITY_IDENTITY_KEY_INVALID") { }
        if (counts != (await db.Users.CountAsync(), await db.AuditEvents.CountAsync())) throw new InvalidOperationException("Rejected identity wrote persistence.");
        Console.WriteLine("Persisted grant-runtime identities retain names/SIDs without claims, legacy role parsing, implicit access or reactivation.");

        async Task<(DefaultHttpContext, RequestAccessContext, bool)> InvokeAsync(ClaimsPrincipal principal)
        {
            var context = new DefaultHttpContext { User = principal };
            var access = new RequestAccessContext(resolver, groups);
            var invoked = false;
            await new GrantIdentityMiddleware(_ => { invoked = true; return Task.CompletedTask; }).InvokeAsync(context,
                service, resolver, access, new RejectBootstrap(), Options.Create(new SecurityOptions()), TimeProvider.System);
            if (context.User.HasClaim(item => item.Type == SecurityClaimTypes.Role || item.Type == SecurityClaimTypes.Permission ||
                item.Type == SecurityClaimTypes.AssetScope || item.Type == SecurityClaimTypes.UserId))
                throw new InvalidOperationException("Identity initialization issued legacy authority claims.");
            return (context, access, invoked);
        }
    }
    private static ClaimsPrincipal Principal(string name, string? sid = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (sid is not null) claims.Add(new Claim(ClaimTypes.PrimarySid, sid));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "HostAuthenticated"));
    }
    private sealed class RejectBootstrap : IGrantBootstrapRepository
    {
        public Task<bool> TryInitializeAdministratorAsync(Guid userId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unlisted identity attempted bootstrap.");
    }
}
