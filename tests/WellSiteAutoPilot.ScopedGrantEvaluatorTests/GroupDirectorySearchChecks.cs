using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

internal static class GroupDirectorySearchChecks
{
    public static async Task RunAsync()
    {
        foreach (var prefix in new[] { "", "a", "*", "ab*", "ab(c)", "DOMAIN\\group", "ab\ncd", new string('a', 101) })
        {
            try { GroupDirectorySearchService.ValidatePrefix(prefix); throw new InvalidOperationException("Unsafe query accepted."); }
            catch (WellSiteAutoPilotException) { }
        }
        var directory = new FakeDirectory();
        var access = new FakeAccess();
        var service = new GroupDirectorySearchService(directory, access);
        var actor = new AccessSubject(Guid.NewGuid());
        await DeniedAsync(() => service.SearchAuthorizedAsync(actor, "Engineering"));
        if (directory.Calls != 0) throw new InvalidOperationException("Directory disclosed to non-administrator.");
        access.Allowed = true;
        var page = await service.SearchAuthorizedAsync(actor, "  Engineering  ");
        if (page.Groups.Single().DirectoryId != "test-tenant" || directory.Prefix != "Engineering")
            throw new InvalidOperationException("Provider-neutral directory result was altered.");
        directory.AfterSearch = () => access.Allowed = false;
        await DeniedAsync(() => service.SearchAuthorizedAsync(actor, "Engineering"));
        access.Allowed = true;
        directory.Supported = false;
        var calls = directory.Calls;
        await DeniedAsync(() => service.SearchAuthorizedAsync(actor, "Engineering"));
        if (directory.Calls != calls) throw new InvalidOperationException("Unsupported directory queried.");
        directory.Supported = true;
        directory.Found = new DirectoryGroup("test-tenant", "OpaqueCaseID", "Engineers", "Example");
        var resolved = await service.ResolveAuthorizedAsync(actor, "test-tenant", "OpaqueCaseID");
        if (resolved.DirectoryId != "test-tenant" || resolved.GroupId != "OpaqueCaseID" || resolved.DisplayName != "Engineers")
            throw new InvalidOperationException("Provider-resolved metadata was lost.");
        await DeniedAsync(async () => { await service.ResolveAuthorizedAsync(actor, "foreign", "OpaqueCaseID"); }, "SECURITY_DIRECTORY_UNSUPPORTED");
        directory.Found = directory.Found with { ExternalId = "opaquecaseid" };
        await DeniedAsync(async () => { await service.ResolveAuthorizedAsync(actor, "test-tenant", "OpaqueCaseID"); }, "SECURITY_DIRECTORY_GROUP_NOT_FOUND");
        directory.Found = null;
        await DeniedAsync(async () => { await service.ResolveAuthorizedAsync(actor, "test-tenant", "deleted"); }, "SECURITY_DIRECTORY_GROUP_NOT_FOUND");
        directory.Found = new DirectoryGroup("test-tenant", "OpaqueCaseID", "Engineers", "Example");
        directory.AfterFind = () => access.Allowed = false;
        await DeniedAsync(async () => { await service.ResolveAuthorizedAsync(actor, "test-tenant", "OpaqueCaseID"); }, "SECURITY_ADMIN_REQUIRED");
        access.Allowed = true;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.SearchAuthorizedAsync(actor, "Engineering", cancelled.Token);
            throw new InvalidOperationException("Cancellation ignored.");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task DeniedAsync(Func<Task> action, string? code = null)
    {
        try { await action(); throw new InvalidOperationException("Directory request unexpectedly succeeded."); }
        catch (WellSiteAutoPilotException exception) when (code is null || exception.Code == code) { }
    }

    private sealed class FakeDirectory : IGroupDirectory
    {
        public string DirectoryId => "test-tenant";
        public bool Supported { get; set; } = true;
        public bool SupportsGroupSearch => Supported;
        public int Calls { get; private set; }
        public string? Prefix { get; private set; }
        public Action? AfterSearch { get; set; }
        public DirectoryGroup? Found { get; set; }
        public Action? AfterFind { get; set; }
        public Task<DirectoryGroup?> FindAsync(string externalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AfterFind?.Invoke();
            return Task.FromResult(Found);
        }
        public Task<GroupDirectoryPage> SearchAsync(string prefix, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; Prefix = prefix; AfterSearch?.Invoke();
            return Task.FromResult(new GroupDirectoryPage([new DirectoryGroup(DirectoryId, "opaque-group-id", "Engineering", "tenant")], false));
        }
    }

    private sealed class FakeAccess : IRequestAccessRepository
    {
        public bool Allowed { get; set; }
        public Task<bool> HasAdministratorAsync(AccessSubject subject, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Allowed);
        }
        public Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAllAssetPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
