using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static partial class AuthorizedSecurityDirectoryChecks
{
    private static async Task CheckAssetDirectoryAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessSubject direct, AccessSubject group, Guid administratorGrant)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var typeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        setup.AssetTypes.Add(new AssetTypeEntity
        {
            Id = typeId, Key = typeId.ToString("N"), DisplayName = "Directory fixture", SchemaVersion = 1,
            IsActive = true, CreatedAtUtc = now
        });
        var assets = Enumerable.Range(0, 3).Select(index => new AssetEntity
        {
            Id = Guid.NewGuid(), AssetTypeId = typeId, Name = "Directory duplicate name",
            IsActive = index != 1, CreatedAtUtc = now, AttributeValuesJson = "{\"private\":\"not-directory-data\"}"
        }).OrderBy(asset => asset.Id).ToArray();
        setup.Assets.AddRange(assets);
        await setup.SaveChangesAsync();
        foreach (var subject in new[] { direct, group })
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var directory = new SecurityDirectoryRepository(db, TimeProvider.System);
            var complete = await directory.ListAssetsAuthorizedAsync(subject, 0, 500);
            var paged = new List<SecurityAssetDirectoryEntry>();
            for (var offset = 0; offset < 3; offset++)
                paged.Add((await directory.ListAssetsAuthorizedAsync(subject, offset, 1)).Single());
            if (!paged.SequenceEqual(complete) || !paged.Select(asset => asset.Id).SequenceEqual(assets.Select(asset => asset.Id)) ||
                !paged.Any(asset => !asset.IsActive) || (await directory.ListAssetsAuthorizedAsync(subject, 3, 1)).Count != 0 ||
                typeof(SecurityAssetDirectoryEntry).GetProperties().Select(property => property.Name)
                    .Except(["Id", "Name", "IsActive"]).Any())
                throw new InvalidOperationException("Protected Asset directory lost stable paging, retired Assets or minimal projection.");
            foreach (var (offset, limit) in new[] { (-1, 1), (1000001, 1), (0, 0), (0, 501) })
                await ExpectAsync(() => directory.ListAssetsAuthorizedAsync(subject, offset, limit), FailureKind.Validation);
        }
        // A protected group administrator can inspect assignment targets even
        // without resource grants. This does not grant resource detail access.
        await using (var db = new WellSiteAutoPilotDbContext(options))
            if (await new AssetRepository(db).GetAuthorizedAsync(group, assets[0].Id) is not null)
                throw new InvalidOperationException("The assignment directory conferred resource read authority.");

        var expiryClock = new AdjustableClock(group.Groups!.ObservedAtUtc);
        var expiryOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
            .AddInterceptors(new ExpireDuringAssetRead(expiryClock)).Options;
        await using (var db = new WellSiteAutoPilotDbContext(expiryOptions))
            await ExpectAsync(() => new SecurityDirectoryRepository(db, expiryClock)
                .ListAssetsAuthorizedAsync(group, 0, 100), FailureKind.Authorization);

        var pause = new PauseAssetRead();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        await using (var db = new WellSiteAutoPilotDbContext(pausedOptions))
        {
            var read = new SecurityDirectoryRepository(db, TimeProvider.System).ListAssetsAuthorizedAsync(direct, 0, 100);
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await setup.Assets.Where(asset => asset.Id == assets[0].Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Name, "Changed after authorization"));
                await setup.AccessGrants.Where(grant => grant.Id == administratorGrant)
                    .ExecuteUpdateAsync(set => set.SetProperty(grant => grant.IsActive, false));
            }
            finally { pause.Release.TrySetResult(); }
            if ((await read).Single(asset => asset.Id == assets[0].Id).Name != assets[0].Name)
                throw new InvalidOperationException("Asset directory mixed newer metadata into an older authority snapshot.");
        }
        await using (var db = new WellSiteAutoPilotDbContext(options))
            await ExpectAsync(() => new SecurityDirectoryRepository(db, TimeProvider.System)
                .ListAssetsAuthorizedAsync(direct, 0, 100), FailureKind.Authorization);
        await setup.AccessGrants.Where(grant => grant.Id == administratorGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(grant => grant.IsActive, true));
        await using (var db = new WellSiteAutoPilotDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await new SecurityDirectoryRepository(db, TimeProvider.System).ListAssetsAuthorizedAsync(direct, 0, 100);
                throw new InvalidOperationException("Asset directory accepted a weak caller transaction.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Authorized security directory reads require a stable database snapshot.") { }
        }
        await setup.Assets.Where(asset => asset.AssetTypeId == typeId).ExecuteDeleteAsync();
        await setup.AssetTypes.Where(type => type.Id == typeId).ExecuteDeleteAsync();
        Console.WriteLine("Protected Asset directory retains stable minimal paging, direct/group authority and snapshot consistency; fresh revocation and hydration proof expiry deny access.");
    }

    private static bool IsAssetRead(DbCommand command) =>
        command.CommandText.Contains("FROM assets.assets AS", StringComparison.Ordinal);

    private sealed class PauseAssetRead : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (IsAssetRead(command))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ExpireDuringAssetRead(AdjustableClock clock) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (IsAssetRead(command)) clock.Now = clock.Now.Add(VerifiedWindowsGroupMembership.MaximumAge).AddTicks(1);
            return ValueTask.FromResult(result);
        }
    }
}
