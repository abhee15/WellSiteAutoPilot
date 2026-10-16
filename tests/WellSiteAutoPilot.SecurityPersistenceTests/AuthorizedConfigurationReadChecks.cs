using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedConfigurationReadChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        foreach (var list in new[] { false, true })
        {
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var grantId = Guid.NewGuid();
            var configId = Guid.NewGuid();
            var assetA = Guid.NewGuid();
            var assetB = Guid.NewGuid();
            await using var setup = new WellSiteAutoPilotDbContext(options);
            var identity = "snapshot-" + userId.ToString("N");
            setup.Users.Add(new UserEntity
            {
                Id = userId, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
                IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
            });
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = roleId, Key = identity, Name = identity, NormalizedName = identity,
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = WellSitePermissions.ConfiguredLogicRead });
            setup.AccessGrants.Add(new AccessGrantEntity
            {
                Id = grantId, UserId = userId, RoleId = roleId, ScopeKind = "ExactAssets", IsActive = true,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            foreach (var id in new[] { assetA, assetB })
                setup.Assets.Add(new AssetEntity { Id = id, AssetTypeId = assetTypeId, Name = identity, IsActive = true, CreatedAtUtc = now });
            setup.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = grantId, AssetId = assetA });
            setup.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity { Id = configId, Name = identity, CreatedAtUtc = now });
            AddRevision(1, assetA);
            await setup.SaveChangesAsync();
            var pause = new PauseAfterScopeQuery();
            var readerOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
            await using var reader = new WellSiteAutoPilotDbContext(readerOptions);
            var repository = new ConfiguredLogicRepository(reader);
            var subject = new AccessSubject(userId);
            var read = list ? ReadPageAsync() : repository.GetAuthorizedAsync(subject, configId);
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                // The predicate already ran, but hydration has not. Commit an
                // otherwise valid revision whose target is outside this grant.
                AddRevision(2, assetB);
                await setup.SaveChangesAsync();
            }
            finally { pause.Release.TrySetResult(); }
            var result = await read;
            if (result is null || result.Revisions.Count != 1 ||
                result.Revisions.SelectMany(revision => revision.AssetBindings).Any(binding => binding.AssetId == assetB))
                throw new InvalidOperationException("Concurrent configuration revision escaped the authorized read snapshot.");
            if (await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, configId) is not null ||
                (await new ConfiguredLogicRepository(setup).ListAuthorizedAsync(subject, 100)).Count != 0)
                throw new InvalidOperationException("A fresh read retained access to a cross-scope configuration.");
            await using (var weak = await setup.Database.BeginTransactionAsync())
            {
                try
                {
                    await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, configId);
                    throw new InvalidOperationException("A weak caller transaction was accepted for configuration hydration.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "Authorized configuration reads require a stable database snapshot.") { }
                await weak.RollbackAsync();
            }
            await setup.AccessGrants.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();
            await setup.ConfiguredLogicDefinitions.Where(config => config.Id == configId).ExecuteDeleteAsync();
            await setup.RolePermissions.Where(permission => permission.RoleId == roleId).ExecuteDeleteAsync();
            await setup.RoleDefinitions.Where(role => role.Id == roleId).ExecuteDeleteAsync();
            await setup.Assets.Where(asset => asset.Id == assetA || asset.Id == assetB).ExecuteDeleteAsync();
            await setup.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();

            void AddRevision(int number, Guid asset)
            {
                var id = Guid.NewGuid();
                setup.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity
                {
                    Id = id, ConfiguredLogicId = configId, RevisionNumber = number,
                    ModuleId = "snapshot-check", ModuleVersion = "1.0.0", ModuleManifestJson = "{}",
                    Mode = "Shadow", Status = "Draft", CreatedAtUtc = now
                });
                setup.ConfiguredLogicAssetBindings.Add(new ConfiguredLogicAssetBindingEntity
                { RevisionId = id, Role = "target", AssetId = asset });
            }
            async Task<WellSiteAutoPilot.Domain.ConfiguredLogic.ConfiguredLogicDefinition?> ReadPageAsync() =>
                (await repository.ListAuthorizedAsync(subject, 100)).SingleOrDefault();
        }
        Console.WriteLine("Authorized configuration list/detail snapshot prevents concurrent revision disclosure; fresh access is denied and weak transactions fail closed.");
    }

    private sealed class PauseAfterScopeQuery : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM logic.configured_logic AS", StringComparison.Ordinal))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
