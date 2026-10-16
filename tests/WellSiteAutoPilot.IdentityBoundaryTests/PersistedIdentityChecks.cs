using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;
using WellSiteAutoPilot.Server.Security;

internal static class PersistedIdentityChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required for identity persistence checks.");
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(connection).Options;
        await using var db = new WellSiteAutoPilotDbContext(options);
        var repository = new UserAccessRepository(db);
        var service = new UserAccessService(repository, TimeProvider.System);
        var assets = new AssetService(new AssetRepository(db), TimeProvider.System);
        var suffix = Guid.NewGuid().ToString("N");
        var type = await assets.CreateAssetTypeAsync(new CreateAssetTypeCommand("identity-" + suffix, "Identity test", "{}"));
        var asset = await assets.CreateAssetAsync(new CreateAssetCommand(type.Id, "Identity test Asset", null, "{}"));
        var name = "IDENTITY\\" + suffix;
        var stableSid = "S-1-5-21-900-901-902-" + Random.Shared.Next(1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var changedSid = stableSid + "-1";
        var resolver = new ConfiguredPrincipalResolver(SecurityOptions.NegotiateAuthenticationMode);
        var noBootstrap = new HashSet<string>(StringComparer.Ordinal);
        var fixtureActor = new SecurityActorContext(null, "system:identity-test", "identity-regression");

        // Pre-existing legacy account keys and assignments are retained.
        var nameProfile = await service.ResolveAuthenticatedAsync("NAME:" + name.ToUpperInvariant(), name, null, noBootstrap);
        await AssignOperatorAsync(nameProfile.Id);
        var (nameContext, nameReached) = await InvokeAsync(Principal(name));
        VerifyOperator(nameContext.User, nameProfile.Id, asset.Id);
        if (!nameReached) throw new InvalidOperationException("Existing name identity did not reach the handler.");

        var sidProfile = await service.ResolveAuthenticatedAsync("SID:" + stableSid, name + "-sid", null, noBootstrap);
        await AssignOperatorAsync(sidProfile.Id);
        var (renamed, sidReached) = await InvokeAsync(Principal(name + "-renamed", stableSid));
        VerifyOperator(renamed.User, sidProfile.Id, asset.Id);
        if (!sidReached || (await repository.GetAsync(sidProfile.Id))?.IdentityName != name + "-renamed")
            throw new InvalidOperationException("Existing SID link did not retain its user and access after a rename.");

        // A SID appearing on a formerly name-only login is a different key.
        // Never borrow that name's old grants or bootstrap a second Admin.
        var appearingSid = stableSid + "-2";
        var (appeared, appearedReached) = await InvokeAsync(Principal(name, appearingSid), [name]);
        var appearedId = UserId(appeared.User);
        VerifyNoAccess(appeared.User);
        if (!appearedReached || appearedId == nameProfile.Id || appearedId == sidProfile.Id)
            throw new InvalidOperationException("SID appearance silently linked existing users.");
        var (changed, changedReached) = await InvokeAsync(Principal(name + "-renamed", changedSid));
        var changedId = UserId(changed.User);
        VerifyNoAccess(changed.User);
        if (!changedReached || changedId == sidProfile.Id || changedId == appearedId)
            throw new InvalidOperationException("Changed SID inherited another subject's account.");
        var (repeated, _) = await InvokeAsync(Principal(name, appearingSid));
        if (UserId(repeated.User) != appearedId)
            throw new InvalidOperationException("Repeated external subject provisioned a duplicate internal user.");
        var persistedName = await repository.GetAsync(nameProfile.Id);
        if (persistedName is null || !persistedName.Roles.SequenceEqual([ApplicationRole.Operator]) ||
            !persistedName.AssetScopeIds.SequenceEqual([asset.Id]))
            throw new InvalidOperationException("SID appearance modified the pre-existing name-only account.");
        foreach (var id in new[] { appearedId, changedId })
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(item =>
                item.Action == "security.user.provisioned" && item.TargetId == id.ToString());
            if (audit.ActorUserId != id || !audit.ActorIdentity.StartsWith("IDENTITY\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Identity provisioning lost the internal audit subject or actor.");
        }

        // An inactive internal user remains denied despite a valid host identity.
        await db.Users.Where(item => item.Id == sidProfile.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        db.ChangeTracker.Clear();
        var (inactive, reached) = await InvokeAsync(Principal(name + "-renamed", stableSid));
        if (reached || inactive.Response.StatusCode != StatusCodes.Status403Forbidden ||
            inactive.User.HasClaim(item => item.Type == SecurityClaimTypes.Permission || item.Type == SecurityClaimTypes.UserId))
            throw new InvalidOperationException("Inactive external subject reached an authorized handler or received access claims.");
        var counts = (await db.Users.CountAsync(), await db.AuditEvents.CountAsync());
        try
        {
            await InvokeAsync(Principal(name, "not-a-sid"));
            throw new InvalidOperationException("Malformed identity reached persistence.");
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "SECURITY_IDENTITY_KEY_INVALID") { }
        if (counts != (await db.Users.CountAsync(), await db.AuditEvents.CountAsync()))
            throw new InvalidOperationException("Rejected external identity left provisioning or audit writes.");
        Console.WriteLine("Real PostgreSQL identity retention, SID changes, claims, inactivity and provisioning audit checks passed.");

        async Task AssignOperatorAsync(Guid id)
        {
            await repository.EnsureRoleAsync(id, ApplicationRole.Operator, fixtureActor, DateTimeOffset.UtcNow);
            db.UserAssetScopes.Add(new UserAssetScopeEntity { UserId = id, AssetId = asset.Id });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        async Task<(DefaultHttpContext Context, bool Reached)> InvokeAsync(ClaimsPrincipal principal, string[]? bootstrap = null)
        {
            var context = new DefaultHttpContext { User = principal };
            var invoked = false;
            var middleware = new UserAccessClaimsMiddleware(_ => { invoked = true; return Task.CompletedTask; });
            await middleware.InvokeAsync(context, service, resolver,
                Options.Create(new SecurityOptions { BootstrapAdministrators = bootstrap ?? [] }));
            return (context, invoked);
        }
    }
    private static ClaimsPrincipal Principal(string name, string? sid = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (sid is not null) claims.Add(new Claim(ClaimTypes.PrimarySid, sid));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "HostAuthenticated"));
    }
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirst(SecurityClaimTypes.UserId)!.Value);
    private static void VerifyOperator(ClaimsPrincipal principal, Guid userId, Guid assetId)
    {
        if (UserId(principal) != userId || !principal.HasClaim(SecurityClaimTypes.Role, "Operator") ||
            !principal.HasClaim(SecurityClaimTypes.Permission, WellSitePermissions.AssetsRead) ||
            !principal.FindAll(SecurityClaimTypes.AssetScope).Select(item => item.Value).SequenceEqual([assetId.ToString()]) ||
            principal.HasClaim(SecurityClaimTypes.Permission, WellSitePermissions.SecurityManage))
            throw new InvalidOperationException("Identity extraction changed legacy role, permission, or Asset claims.");
    }
    private static void VerifyNoAccess(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(item => item.Type == SecurityClaimTypes.Role || item.Type == SecurityClaimTypes.Permission ||
            item.Type == SecurityClaimTypes.AssetScope))
            throw new InvalidOperationException("A new external subject inherited existing product access.");
    }
}
