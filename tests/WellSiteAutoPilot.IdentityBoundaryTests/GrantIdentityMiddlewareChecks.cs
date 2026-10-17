using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Server.Security;

internal static class GrantIdentityMiddlewareChecks
{
    public static async Task RunAsync()
    {
        var identity = new IdentityRepository();
        var bootstrap = new Bootstrap();
        var principals = new ConfiguredPrincipalResolver("TestHeader");
        var groups = new RejectGroups();
        var options = Options.Create(new SecurityOptions { BootstrapAdministrators = ["  corp\\BOOTSTRAP  "] });
        var directory = new DirectoryNames();
        var nextCalls = 0;
        var middleware = new GrantIdentityMiddleware(_ => { nextCalls++; return Task.CompletedTask; });

        var anonymous = new DefaultHttpContext();
        await InvokeAsync(anonymous, new RequestAccessContext(principals, groups));
        Require(nextCalls == 1 && identity.Calls == 0 && bootstrap.Calls == 0,
            "Anonymous requests attempted provisioning or bootstrap.");

        var regular = Context("CORP\\Operator");
        var regularAccess = new RequestAccessContext(principals, groups);
        var claims = regular.User.Claims.ToArray();
        await InvokeAsync(regular, regularAccess);
        Require(directory.Calls == 1 && identity.LastDisplayName == "Directory Person",
            "A login without a provider display name did not receive the directory name.");
        Require(nextCalls == 2 && identity.Calls == 1 && bootstrap.Calls == 0 &&
            regularAccess.Subject.UserId == identity.UserId && regularAccess.Subject.WindowsGroups is null &&
            regular.User.Claims.SequenceEqual(claims) && regular.User.Identities.Count() == 1,
            "Identity provisioning issued claims, Windows group proof or implicit bootstrap access.");
        Require(identity.LastKey == "NAME:CORP\\OPERATOR" && identity.LastNormalizedName == "CORP\\OPERATOR",
            "Provider identity was not normalized for identity-only persistence.");

        identity.Active = false;
        var disabled = Context("CORP\\Bootstrap");
        var disabledAccess = new RequestAccessContext(principals, groups);
        await InvokeAsync(disabled, disabledAccess);
        Require(disabled.Response.StatusCode == StatusCodes.Status403Forbidden && nextCalls == 2 &&
            bootstrap.Calls == 0 && !disabledAccess.TryGetSubject(disabled.User, out _),
            "Disabled allowlisted user was bootstrapped or admitted.");

        identity.Active = true;
        var admin = Context("corp\\Bootstrap");
        var adminAccess = new RequestAccessContext(principals, groups);
        using var aborted = new CancellationTokenSource();
        admin.RequestAborted = aborted.Token;
        await InvokeAsync(admin, adminAccess);
        Require(nextCalls == 3 && bootstrap.Calls == 1 && bootstrap.LastUserId == identity.UserId &&
            bootstrap.LastCancellation == aborted.Token && identity.LastCancellation == aborted.Token &&
            !admin.User.HasClaim(claim => claim.Type == SecurityClaimTypes.Permission ||
                claim.Type == SecurityClaimTypes.Role || claim.Type == SecurityClaimTypes.AssetScope),
            "Explicit bootstrap lost its persisted subject/cancellation or issued claims.");
        // The repository owns one-time initialization and recovery protection.
        // A false result must never trigger a second, legacy bootstrap path.
        var repeated = Context("CORP\\Bootstrap");
        await InvokeAsync(repeated, new RequestAccessContext(principals, groups));
        Require(nextCalls == 4 && bootstrap.Calls == 2,
            "An initialized deployment did not continue without implicit regrant.");
        // Identity and bootstrap persistence errors must abort the request, never
        // fall through to downstream endpoints with a partially bound subject.
        identity.ThrowOnUpsert = true;
        var failedIdentity = Context("CORP\\Operator");
        await RequireFailureAsync(() => InvokeAsync(failedIdentity,
            new RequestAccessContext(principals, groups)), "Identity provisioning failure");
        Require(nextCalls == 4 && bootstrap.Calls == 2,
            "Identity persistence failure allowed a request or attempted bootstrap.");
        identity.ThrowOnUpsert = false;

        bootstrap.ThrowOnAttempt = true;
        var failedBootstrap = Context("CORP\\Bootstrap");
        var failedBootstrapAccess = new RequestAccessContext(principals, groups);
        await RequireFailureAsync(() => InvokeAsync(failedBootstrap, failedBootstrapAccess),
            "Administrator bootstrap failure");
        Require(nextCalls == 4 && bootstrap.Calls == 3 &&
            failedBootstrapAccess.Subject.UserId == identity.UserId,
            "Bootstrap persistence failure allowed downstream execution or lost bound identity.");
        bootstrap.ThrowOnAttempt = false;
        Console.WriteLine("Grant identity middleware separates anonymous, inactive, ordinary and explicitly allowlisted identities without issuing authority claims.");

        static async Task RequireFailureAsync(Func<Task> action, string label)
        {
            try { await action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(label + " unexpectedly succeeded.");
        }

        Task InvokeAsync(HttpContext http, RequestAccessContext access) => middleware.InvokeAsync(http,
            new UserIdentityService(identity, TimeProvider.System), principals, access, bootstrap, options, TimeProvider.System,
            directory);
    }

    private static DefaultHttpContext Context(string name) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test-provider"))
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DirectoryNames : IDirectoryDisplayNameResolver
    {
        public int Calls { get; private set; }
        public Task<string?> ResolveAsync(ClaimsPrincipal principal, AuthenticatedExternalIdentity externalIdentity,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<string?>("Directory Person");
        }
    }

    private sealed class RejectGroups : IIdentityGroupResolver
    {
        public VerifiedWindowsGroupMembership? Resolve(ClaimsPrincipal principal,
            AuthenticatedExternalIdentity identity, Guid localUserId) =>
            throw new InvalidOperationException("TestHeader cannot provide Windows group authority.");
    }

    private sealed class Bootstrap : IGrantBootstrapRepository
    {
        public int Calls { get; private set; }
        public bool ThrowOnAttempt { get; set; }
        public Guid LastUserId { get; private set; }
        public CancellationToken LastCancellation { get; private set; }
        public Task<bool> TryInitializeAdministratorAsync(Guid userId, DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnAttempt) throw new InvalidOperationException("Injected bootstrap persistence failure.");
            LastUserId = userId;
            LastCancellation = cancellationToken;
            return Task.FromResult(Calls == 1);
        }
    }

    private sealed class IdentityRepository : IUserIdentityRepository
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public bool Active { get; set; } = true;
        public bool ThrowOnUpsert { get; set; }
        public int Calls { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastNormalizedName { get; private set; }
        public string? LastDisplayName { get; private set; }
        public CancellationToken LastCancellation { get; private set; }
        public Task<UserIdentity> UpsertIdentityAsync(string identityKey, string identityName, string normalizedIdentityName,
            string? displayName, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnUpsert) throw new InvalidOperationException("Injected identity persistence failure.");
            LastKey = identityKey;
            LastNormalizedName = normalizedIdentityName;
            LastDisplayName = displayName;
            LastCancellation = cancellationToken;
            return Task.FromResult(new UserIdentity(UserId, identityKey, identityName, displayName, Active, seenAtUtc, seenAtUtc));
        }

        public Task<UserIdentity?> GetIdentityAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provisioning must not load legacy access profiles.");
        public Task<IReadOnlyCollection<UserIdentity>> ListIdentitiesAsync(int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provisioning must not enumerate users.");
    }
}
