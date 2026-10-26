using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;

internal static class AuthorizedModulePackageChecks
{
    public static async Task<int> RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessSubject subject, SecurityActorContext actor, Guid globalGrant, string sourceManifest)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var before = await setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId);
        var manifest = JsonNode.Parse(sourceManifest)!.AsObject();
        var moduleId = actor.IdentityName + ".packages";
        manifest["moduleId"] = moduleId;
        manifest["version"] = "1.0.0";
        var original = Pack(manifest.ToJsonString(), "first payload");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(original));
        // A resource-wide grant is still insufficient for package upload.
        await setup.AccessGrants.Where(item => item.Id == globalGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ScopeKind, "AllAssets"));
        await ExpectAsync(() => UploadAsync(original), "SECURITY_SCOPE_DENIED", FailureKind.Authorization);
        if (await setup.LogicModulePackages.AnyAsync(item => item.Sha256 == checksum))
            throw new InvalidOperationException("Unauthorized upload persisted package bytes.");
        await setup.AccessGrants.Where(item => item.Id == globalGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ScopeKind, "DeploymentWide"));
        var uploaded = await UploadAsync(original);
        if (uploaded.IsEnabled || uploaded.TrustStatus != LogicModuleTrustStatus.Untrusted || uploaded.PackageSha256 != checksum)
            throw new InvalidOperationException("Upload supplied its own trust or checksum.");
        var stored = await setup.LogicModulePackages.AsNoTracking().SingleAsync(item => item.Sha256 == checksum);
        if (!stored.Content.SequenceEqual(original) || stored.UploadedByUserId != subject.UserId ||
            (await UploadAsync(original)).Id != uploaded.Id ||
            await setup.AuditEvents.CountAsync(item => item.TargetId == uploaded.Id.ToString()) != 2)
            throw new InvalidOperationException("Upload did not retain immutable bytes and exactly one registration/upload audit.");
        await ExpectAsync(() => UploadAsync(Pack(manifest.ToJsonString(), "changed bytes")), "MODULE_VERSION_IMMUTABLE", FailureKind.Conflict);
        async Task<int> QueueCountAsync() => await setup.OutboxMessages.CountAsync(item => item.Type == ModulePackagePublication.OutboxType && item.Payload.Contains(uploaded.Id.ToString("D")));
        if (await QueueCountAsync() != 0) throw new InvalidOperationException("Upload queued unapproved bytes.");
        var approvalFailure = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        await using (var failedApproval = new WellSiteAutoPilotDbContext(approvalFailure))
        {
            try
            {
                await new LogicModuleApprovalService(new LogicModuleCatalogRepository(failedApproval))
                    .ApproveAsync(moduleId, uploaded.Version, "Rollback probe", subject, actor);
                throw new InvalidOperationException("Approval failure injection did not run.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "package-save-probe") { }
        }
        if (await QueueCountAsync() != 0 ||
            (await setup.LogicModules.AsNoTracking().SingleAsync(item => item.Id == uploaded.Id)).IsEnabled ||
            await setup.AuditEvents.AnyAsync(item => item.TargetId == uploaded.Id.ToString() && item.Action == "logic-module.approved"))
            throw new InvalidOperationException("Failed approval committed trust, audit or a publication request.");
        await ChangeAsync(uploaded.Version, true);
        await ChangeAsync(uploaded.Version, true);
        if (await QueueCountAsync() != 1) throw new InvalidOperationException("Approval did not queue exactly one publication.");
        if (!(await UploadAsync(original)).IsEnabled) throw new InvalidOperationException("Repeated upload reset approval.");
        await ChangeAsync(uploaded.Version, false);
        if ((await UploadAsync(original)).IsEnabled) throw new InvalidOperationException("Repeated upload re-enabled a retired version.");

        // Previously approved metadata cannot acquire executable bytes after review.
        manifest["version"] = "1.0.1";
        var late = Pack(manifest.ToJsonString(), "late bytes");
        await using (var registration = new WellSiteAutoPilotDbContext(options))
        {
            await new LogicModuleCatalogService(new LogicModuleCatalogRepository(registration), TimeProvider.System)
                .RegisterAuthorizedAsync(new(manifest.ToJsonString(), Convert.ToHexStringLower(SHA256.HashData(late)),
                    LogicModuleTrustStatus.Untrusted), subject, actor);
        }
        await ChangeAsync("1.0.1", true);
        await ExpectAsync(() => UploadAsync(late), "MODULE_PACKAGE_REQUIRES_NEW_VERSION", FailureKind.Conflict);

        // A save failure rolls bytes, catalog and audit back together.
        manifest["version"] = "1.0.2";
        var failed = Pack(manifest.ToJsonString(), "rolled back");
        var failing = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        try { await UploadAsync(failed, failing); throw new InvalidOperationException("Failure injection did not run."); }
        catch (InvalidOperationException exception) when (exception.Message == "package-save-probe") { }
        var failedHash = Convert.ToHexStringLower(SHA256.HashData(failed));
        if (await setup.LogicModulePackages.AnyAsync(item => item.Sha256 == failedHash) ||
            await setup.LogicModules.AnyAsync(item => item.ModuleId == moduleId && item.Version == "1.0.2"))
            throw new InvalidOperationException("Failed upload left a partial catalog/package.");

        manifest["version"] = "1.0.3";
        var concurrent = Pack(manifest.ToJsonString(), "concurrent");
        async Task RaceAsync()
        {
            try { await UploadAsync(concurrent); }
            catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_CATALOG_CONFLICT") { }
        }
        await Task.WhenAll(RaceAsync(), RaceAsync());
        var winner = await UploadAsync(concurrent);
        if (await setup.LogicModules.CountAsync(item => item.ModuleId == moduleId && item.Version == "1.0.3") != 1 ||
            await setup.LogicModulePackages.CountAsync(item => item.Sha256 == winner.PackageSha256) != 1 ||
            await setup.AuditEvents.CountAsync(item => item.TargetId == winner.Id.ToString()) != 2)
            throw new InvalidOperationException("Concurrent upload duplicated package, module or audit.");
        await setup.AccessGrants.Where(item => item.Id == globalGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ScopeKind, "AllAssets"));
        await ExpectAsync(() => UploadAsync(original), "SECURITY_SCOPE_DENIED", FailureKind.Authorization);
        await setup.AccessGrants.Where(item => item.Id == globalGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ScopeKind, "DeploymentWide"));
        var count = await setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId) - before;
        await setup.OutboxMessages.Where(item => item.Type == ModulePackagePublication.OutboxType && item.Payload.Contains(moduleId)).ExecuteDeleteAsync();
        await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteDeleteAsync();
        await setup.LogicModulePackages.Where(item => item.Sha256 == checksum || item.Sha256 == winner.PackageSha256).ExecuteDeleteAsync();
        Console.WriteLine("Package upload: global authority, immutable bytes/trust, concurrency and atomic rollback checks passed.");
        return count;

        async Task<InstalledLogicModule> UploadAsync(byte[] bytes, DbContextOptions<WellSiteAutoPilotDbContext>? configuration = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(configuration ?? options);
            using var input = new MemoryStream(bytes);
            return await new LogicModulePackageService(new LogicModuleCatalogRepository(db), TimeProvider.System)
                .UploadAsync(input, subject, actor);
        }
        async Task ChangeAsync(string version, bool approve)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var service = new LogicModuleApprovalService(new LogicModuleCatalogRepository(db));
            if (approve) await service.ApproveAsync(moduleId, version, "Reviewed test package", subject, actor);
            else await service.RetireAsync(moduleId, version, "Superseded test package", subject, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> action, string code, FailureKind kind)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind && (kind == FailureKind.Authorization || exception.Code == code)) { return; }
        throw new InvalidOperationException($"Expected package refusal {code}.");
    }

    private static byte[] Pack(string manifest, string payload)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(manifest);
            using (var writer = new StreamWriter(archive.CreateEntry("payload/main.py").Open())) writer.Write(payload);
        }
        return stream.ToArray();
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("package-save-probe");
    }
}
