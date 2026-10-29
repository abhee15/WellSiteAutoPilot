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
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.SearchAuthorizedAsync(actor, "Engineering", cancelled.Token);
            throw new InvalidOperationException("Cancellation ignored.");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task DeniedAsync(Func<Task<GroupDirectoryPage>> action)
    {
        try { await action(); throw new InvalidOperationException("Directory request unexpectedly succeeded."); }
        catch (WellSiteAutoPilotException) { }
    }

    private sealed class FakeDirectory : IGroupDirectory
    {
        public string DirectoryId => "test-tenant";
        public bool Supported { get; set; } = true;
        public bool SupportsGroupSearch => Supported;
        public int Calls { get; private set; }
        public string? Prefix { get; private set; }
        public Action? AfterSearch { get; set; }
        public Task<DirectoryGroup?> FindAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult<DirectoryGroup?>(null);
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
