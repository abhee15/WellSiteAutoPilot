using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Net;
using Npgsql;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Security;
using WellSiteAutoPilot.Worker.DotNet.Execution;

internal static class ModulePackagePublicationChecks
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, NatsConnection connection)
    {
        var user = Guid.NewGuid();
        var role = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var identity = "package-publication-" + user.ToString("N");
        var subject = new AccessSubject(user);
        var actor = new SecurityActorContext(user, identity, identity);
        await using var setup = new WellSiteAutoPilotDbContext(options);
        setup.Users.Add(new UserEntity { Id = user, IdentityKey = identity, IdentityName = identity,
            NormalizedIdentityName = identity, IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
        setup.RoleDefinitions.Add(new RoleDefinitionEntity { Id = role, Key = identity, Name = identity,
            NormalizedName = identity, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.LogicManage });
        setup.AccessGrants.Add(new AccessGrantEntity { Id = Guid.NewGuid(), UserId = user, RoleId = role,
            SubjectKind = "User", ScopeKind = "DeploymentWide", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        await setup.SaveChangesAsync();
        var objectStore = new NatsModulePackageObjectStore(connection);
        var firstBytes = Pack(identity, "1.0.0");
        var first = await UploadAsync(firstBytes);
        var uploadedState = await new LogicModuleCatalogRepository(setup).ReadPackageStateAsync(first.Id, first.PackageSha256);
        Assert(uploadedState.IsStored && !uploadedState.PublicationRecorded && !uploadedState.PublicationPending,
            "Unapproved archive reported delivery.");
        Assert(await QueuesAsync(first.Id) == 0, "Upload queued unapproved code.");
        await ApproveAsync(first.Version);
        await ApproveAsync(first.Version);
        Assert(await QueuesAsync(first.Id) == 1, "Repeated approval duplicated publication.");
        var firstQueue = await QueueAsync(first.Id);
        await new OutboxPublisher(setup, connection, TimeProvider.System).DispatchBatchAsync();
        Assert(!await ProcessedAsync(firstQueue), "Execution dispatcher consumed a package publication.");

        var beforeWrite = new FaultingStore(objectStore, afterWrite: false);
        await ExpectFailureAsync(() => DispatchAsync(firstQueue, beforeWrite));
        Assert(!await ProcessedAsync(firstQueue), "Failed transport consumed the publication.");
        var afterWrite = new FaultingStore(objectStore, afterWrite: true);
        await ExpectFailureAsync(() => DispatchAsync(firstQueue, afterWrite));
        Assert(!await ProcessedAsync(firstQueue) && await PublishedAuditsAsync(first.Id) == 0,
            "Post-write failure committed processed state/audit.");
        var failedCommitOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
            .AddInterceptors(new RejectPublicationSave()).Options;
        try
        {
            await DispatchAsync(firstQueue, objectStore, failedCommitOptions);
            throw new InvalidOperationException("Publication save failure injection did not run.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "publication-commit-probe") { }
        Assert(!await ProcessedAsync(firstQueue) && await PublishedAuditsAsync(first.Id) == 0,
            "Database failure after broker delivery committed processed state or audit.");
        Assert(await DispatchAsync(firstQueue, objectStore), "Queued publication did not recover.");
        var publishedState = await new LogicModuleCatalogRepository(setup).ReadPackageStateAsync(first.Id, first.PackageSha256);
        Assert(publishedState.PublicationRecorded && !publishedState.PublicationPending &&
            await objectStore.ContainsAsync(first.PackageSha256) && !await objectStore.ContainsAsync(new string('0', 64)),
            "Published package readiness did not reflect recorded delivery and exact artifact availability.");
        Assert(!await DispatchAsync(firstQueue, objectStore) && await PublishedAuditsAsync(first.Id) == 1,
            "A processed publication was sent or audited twice.");
        var store = await connection.CreateObjectStoreContext().GetObjectStoreAsync(NatsModulePackageObjectStore.BucketName);
        using (var downloaded = new MemoryStream())
        {
            await store.GetAsync("sha256/" + first.PackageSha256, downloaded);
            Assert(firstBytes.SequenceEqual(downloaded.ToArray()), "Published package bytes changed.");
        }
        using (var workerDownload = new MemoryStream())
        {
            await new NatsModulePackageSource(connection).CopyToAsync(first.PackageSha256, workerDownload, CancellationToken.None);
            Assert(firstBytes.SequenceEqual(workerDownload.ToArray()), "Worker's exact-hash package retrieval changed approved bytes.");
            Assert(workerDownload.CanRead && workerDownload.CanWrite, "Worker transport closed the host-owned package buffer.");
        }
        using (var missingDownload = new MemoryStream())
        {
            try
            {
                await new NatsModulePackageSource(connection).CopyToAsync(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), missingDownload, CancellationToken.None);
                throw new InvalidOperationException("Missing package was accepted.");
            }
            catch (WellSiteAutoPilotException error) when (error.Code == "MODULE_PACKAGE_NOT_AVAILABLE") { }
        }

        var retired = await UploadAsync(Pack(identity, "1.0.1"));
        await ApproveAsync(retired.Version);
        await RetireAsync(retired.Version);
        var noWrite = new RecordingStore();
        Assert(await DispatchAsync(await QueueAsync(retired.Id), noWrite) && noWrite.Calls == 0,
            "An already retired version was published.");
        Assert(await setup.AuditEvents.AnyAsync(item => item.TargetId == retired.Id.ToString() &&
            item.Action == "logic-module.package-publication-skipped"), "Retirement skip was not audited.");

        var corruptBytes = Pack(identity, "1.0.2");
        var corrupt = await UploadAsync(corruptBytes);
        await ApproveAsync(corrupt.Version);
        await setup.LogicModulePackages.Where(item => item.Sha256 == corrupt.PackageSha256)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Content, new byte[] { 1, 2 }));
        var corruptQueue = await QueueAsync(corrupt.Id);
        try { await DispatchAsync(corruptQueue, noWrite); throw new InvalidOperationException("Corrupt bytes were accepted."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_PACKAGE_HASH_MISMATCH") { }
        Assert(noWrite.Calls == 0 && !await ProcessedAsync(corruptQueue), "Corrupt package escaped or consumed its request.");
        await setup.LogicModulePackages.Where(item => item.Sha256 == corrupt.PackageSha256)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Content, corruptBytes));
        await DispatchAsync(corruptQueue, objectStore);

        // A second dispatcher skips the claimed row, and retirement waits until
        // the in-flight authorized publication commits before disabling the module.
        var fenced = await UploadAsync(Pack(identity, "1.0.3"));
        await ApproveAsync(fenced.Version);
        var fencedQueue = await QueueAsync(fenced.Id);
        var blocking = new BlockingStore(objectStore);
        var publishing = DispatchAsync(fencedQueue, blocking);
        Task? retiring = null;
        try
        {
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert(!await DispatchAsync(fencedQueue, noWrite), "Two dispatchers claimed the same publication.");
            var retirementConnection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
                { ApplicationName = identity, CommandTimeout = 15 };
            var retirementOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(retirementConnection.ConnectionString).Options;
            retiring = RetireAsync(fenced.Version, retirementOptions);
            await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
            await observer.OpenAsync();
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = $1
                AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0)
                """, observer);
            waiting.Parameters.AddWithValue(identity);
            var timer = Stopwatch.StartNew();
            while (!Equals(await waiting.ExecuteScalarAsync(), true))
            {
                Assert(!retiring.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(8), "Retirement was not fenced by publication.");
                await Task.Delay(20);
            }
        }
        finally { blocking.Release.TrySetResult(); await publishing; if (retiring is not null) await retiring; }
        Assert(await ProcessedAsync(fencedQueue) &&
            !(await setup.LogicModules.AsNoTracking().SingleAsync(item => item.Id == fenced.Id)).IsEnabled,
            "Publication/retirement fencing did not finish consistently.");
        Assert(noWrite.Calls == 0, "A duplicate dispatcher wrote an object.");
        // Simulate an approved package persisted by the preceding release,
        // which had no publication queue. Repeat approval repairs delivery only.
        var legacy = await UploadAsync(Pack(identity, "1.0.4"));
        await ApproveAsync(legacy.Version);
        await setup.OutboxMessages.Where(item => item.Id == legacy.Id).ExecuteDeleteAsync();
        async Task RepairAsync()
        {
            try { await ApproveAsync(legacy.Version); }
            catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_CATALOG_CONFLICT") { }
        }
        await Task.WhenAll(RepairAsync(), RepairAsync());
        await ApproveAsync(legacy.Version);
        Assert(await QueuesAsync(legacy.Id) == 1 &&
            await setup.AuditEvents.CountAsync(item => item.TargetId == legacy.Id.ToString() && item.Action == "logic-module.approved") == 1 &&
            await setup.AuditEvents.CountAsync(item => item.TargetId == legacy.Id.ToString() && item.Action == "logic-module.package-publication-queued") == 1,
            "Repeat approval did not repair missing delivery idempotently.");
        await DispatchAsync(await QueueAsync(legacy.Id), objectStore);
        // More than a complete batch of poison work must not starve a later approved package.
        var poison = Enumerable.Range(0, 21).Select(_ => new WellSiteAutoPilot.Persistence.Messaging.OutboxMessageEntity
        { Id = Guid.NewGuid(), Type = ModulePackagePublication.OutboxType, Payload = "{}", CreatedAtUtc = now.AddDays(-1) }).ToArray();
        setup.OutboxMessages.AddRange(poison);
        await setup.SaveChangesAsync();
        var later = await UploadAsync(Pack(identity, "1.0.5"));
        await ApproveAsync(later.Version);
        var fairStore = new RecordingStore();
        await using (var fairDb = new WellSiteAutoPilotDbContext(options))
        {
            var publisher = new ModulePackagePublisher(fairDb, fairStore, TimeProvider.System,
                NullLogger<ModulePackagePublisher>.Instance);
            await publisher.DispatchBatchAsync();
            Assert(await fairDb.OutboxMessages.CountAsync(item => poison.Select(p => p.Id).Contains(item.Id) &&
                item.ProcessedAtUtc == null && item.AttemptCount == 1 && item.NextAttemptAtUtc != null && item.LastFailureCode != null) >= 20,
                "Poison publication retries were not durably scheduled with bounded diagnostics.");
            await publisher.DispatchBatchAsync();
        }
        Assert(await ProcessedAsync(await QueueAsync(later.Id)) && fairStore.Calls >= 1,
            "A full poison batch starved a later approved package.");
        await setup.OutboxMessages.Where(item => poison.Select(p => p.Id).Contains(item.Id)).ExecuteDeleteAsync();
        Console.WriteLine("Approved package delivery: retry/replay, checksum, retirement fencing and real multi-chunk Object Store checks passed.");

        async Task<InstalledLogicModule> UploadAsync(byte[] bytes)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            using var input = new MemoryStream(bytes);
            return await new LogicModulePackageService(new LogicModuleCatalogRepository(db), TimeProvider.System).UploadAsync(input, subject, actor);
        }
        async Task ApproveAsync(string version)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new LogicModuleApprovalService(new LogicModuleCatalogRepository(db)).ApproveAsync(identity, version, "Reviewed fixture", subject, actor);
        }
        async Task RetireAsync(string version, DbContextOptions<WellSiteAutoPilotDbContext>? dbOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(dbOptions ?? options);
            await new LogicModuleApprovalService(new LogicModuleCatalogRepository(db)).RetireAsync(identity, version, "Retirement fixture", subject, actor);
        }
        async Task<bool> DispatchAsync(Guid id, IModulePackageObjectStore sink,
            DbContextOptions<WellSiteAutoPilotDbContext>? publicationOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(publicationOptions ?? options);
            return await new ModulePackagePublisher(db, sink, TimeProvider.System, NullLogger<ModulePackagePublisher>.Instance).DispatchAsync(id);
        }
        Task<int> QueuesAsync(Guid moduleId) => setup.OutboxMessages.CountAsync(item => item.Type == ModulePackagePublication.OutboxType && item.Payload.Contains(moduleId.ToString("D")));
        async Task<Guid> QueueAsync(Guid moduleId) => await setup.OutboxMessages.Where(item => item.Type == ModulePackagePublication.OutboxType && item.Payload.Contains(moduleId.ToString("D"))).Select(item => item.Id).SingleAsync();
        Task<bool> ProcessedAsync(Guid id) => setup.OutboxMessages.AnyAsync(item => item.Id == id && item.ProcessedAtUtc != null);
        Task<int> PublishedAuditsAsync(Guid id) => setup.AuditEvents.CountAsync(item => item.TargetId == id.ToString() && item.Action == "logic-module.package-published");
    }

    private static byte[] Pack(string moduleId, string version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var manifest = new LogicModuleManifest(1, moduleId, version, "Package fixture", "Test", LogicRuntimeKind.Python,
                ExecutionProfile.ScheduledOneShot, "main:execute", "1.1.0", [new("well", 1, 1, "well", [])], [], []);
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, SerializerOptions));
            var payload = new byte[1024 * 1024 + 123];
            RandomNumberGenerator.Fill(payload);
            using var content = archive.CreateEntry("payload/main.py", CompressionLevel.NoCompression).Open();
            content.Write(payload);
        }
        return stream.ToArray();
    }
    private static async Task ExpectFailureAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (IOException exception) when (exception.Message == "publication-probe") { return; }
        throw new InvalidOperationException("The publication failure fixture did not run.");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class RejectPublicationSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("publication-commit-probe");
    }
    private sealed class FaultingStore(IModulePackageObjectStore inner, bool afterWrite) : IModulePackageObjectStore
    {
        public async Task PutAsync(string packageSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            if (afterWrite) await inner.PutAsync(packageSha256, content, cancellationToken);
            throw new IOException("publication-probe");
        }
    }
    private sealed class RecordingStore : IModulePackageObjectStore
    {
        public int Calls { get; private set; }
        public Task PutAsync(string packageSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
    private sealed class BlockingStore(IModulePackageObjectStore inner) : IModulePackageObjectStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PutAsync(string packageSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            await inner.PutAsync(packageSha256, content, cancellationToken);
        }
    }
}
