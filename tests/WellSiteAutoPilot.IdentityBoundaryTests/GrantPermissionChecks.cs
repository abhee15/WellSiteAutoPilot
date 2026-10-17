using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Server.Security;

internal static class GrantPermissionChecks
{
    public static async Task RunAsync()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "CORP\\Operator"),
            new Claim(SecurityClaimTypes.Role, "Admin"),
            new Claim(SecurityClaimTypes.AssetScope, "*"),
            .. WellSitePermissions.All.Select(permission => new Claim(SecurityClaimTypes.Permission, permission))
        ], "test-provider"));
        var membership = new VerifiedWindowsGroupMembership(userId, ["S-1-5-21-1201"], now);
        var access = new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), new Groups(membership));
        var repository = new Repository();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(WellSitePolicies.AddGrantPolicies);
        services.AddSingleton(access);
        services.AddSingleton<IRequestAccessRepository>(repository);
        services.AddSingleton<IAuthorizationHandler, GrantPermissionHandler>();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        using var abort = new CancellationTokenSource();
        var http = new DefaultHttpContext { User = principal, RequestAborted = abort.Token };

        await ExpectAsync(WellSitePermissions.AssetsRead, false);
        Require(repository.Calls == 0, "Unbound claims reached grant authority.");
        access.Initialize(principal, new UserIdentity(userId, "NAME:CORP\\OPERATOR", "CORP\\Operator", null, true, now, now));

        // Spoofed claims never satisfy a policy, including a protected Admin.
        foreach (var permission in WellSitePermissions.All) await ExpectAsync(permission, false);
        repository.Hints = WellSitePermissions.All;
        foreach (var definition in ProductPermissionCatalog.Definitions)
            await ExpectAsync(definition.Key, definition.ScopeKind == "ResourceScoped");
        Require(repository.AdminCalls > 0 && repository.DeploymentCalls > 0,
            "Global or protected policies accepted coarse resource hints.");

        repository.DeploymentAllowed = true;
        await ExpectAsync(WellSitePermissions.LogicRead, true);
        await ExpectAsync(WellSitePermissions.LogicManage, true);
        await ExpectAsync(WellSitePermissions.SecurityManage, false);
        repository.Administrator = true;
        await ExpectAsync(WellSitePermissions.SecurityManage, true);
        Require(ReferenceEquals(repository.LastSubject, access.Subject) &&
            ReferenceEquals(repository.LastSubject.WindowsGroups, membership) && repository.LastCancellation == abort.Token,
            "The handler lost its bound subject, group observation or request cancellation.");

        // The same context must query again after revocation, not cache claims
        // or a prior successful policy result for the rest of the request.
        repository.Hints = [];
        repository.DeploymentAllowed = false;
        repository.Administrator = false;
        foreach (var permission in WellSitePermissions.All) await ExpectAsync(permission, false);
        repository.Hints = WellSitePermissions.All;
        var calls = repository.Calls;
        var replacement = new ClaimsPrincipal(principal);
        Require(!(await authorization.AuthorizeAsync(replacement, http, WellSitePermissions.AssetsRead)).Succeeded,
            "A replacement principal inherited another principal's bound subject.");
        Require(!(await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), http,
            WellSitePermissions.AssetsRead)).Succeeded, "Anonymous access was accepted.");
        Require(!(await authorization.AuthorizeAsync(principal, http,
            [new GrantPermissionRequirement("unknown.permission")])).Succeeded, "Unknown permission was accepted.");
        Require(repository.Calls == calls, "Invalid principals or permissions reached grant queries.");
        Console.WriteLine("Grant policies reject spoofed claims, require bound identity, distinguish protected/global/resource checks and requery after revocation.");

        async Task ExpectAsync(string permission, bool allowed) =>
            Require((await authorization.AuthorizeAsync(principal, http, permission)).Succeeded == allowed,
                $"Unexpected grant policy decision for {permission}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Groups(VerifiedWindowsGroupMembership membership) : IIdentityGroupResolver
    {
        public VerifiedWindowsGroupMembership? Resolve(ClaimsPrincipal principal,
            AuthenticatedExternalIdentity identity, Guid localUserId) => membership;
    }

    private sealed class Repository : IRequestAccessRepository
    {
        public IReadOnlyCollection<string> Hints { get; set; } = [];
        public bool Administrator { get; set; }
        public bool DeploymentAllowed { get; set; }
        public int Calls { get; private set; }
        public int AdminCalls { get; private set; }
        public int DeploymentCalls { get; private set; }
        public AccessSubject? LastSubject { get; private set; }
        public CancellationToken LastCancellation { get; private set; }

        public Task<bool> HasAdministratorAsync(AccessSubject subject, CancellationToken cancellationToken = default)
        {
            Observe(subject, cancellationToken);
            AdminCalls++;
            return Task.FromResult(Administrator);
        }

        public Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject, CancellationToken cancellationToken = default)
        {
            Observe(subject, cancellationToken);
            return Task.FromResult(Hints);
        }

        public Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default)
        {
            Observe(subject, cancellationToken);
            DeploymentCalls++;
            return Task.FromResult(DeploymentAllowed);
        }

        public Task<bool> HasAllAssetPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A coarse route policy must not invent AllAssets authority.");

        public Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission,
            IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A coarse route policy must not replace resource authorization.");

        private void Observe(AccessSubject subject, CancellationToken cancellationToken)
        {
            Calls++;
            LastSubject = subject;
            LastCancellation = cancellationToken;
        }
    }
}
