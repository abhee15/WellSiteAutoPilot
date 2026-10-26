using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class PackageHostChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wsa-host-test-" + Guid.NewGuid().ToString("N"));
        var marker = root + ".marker";
        Environment.SetEnvironmentVariable("WSA_PACKAGE_HOST_TEST_MARKER", marker);
        try
        {
            var manifest = new LogicModuleManifest(1, PackageHostProbeModule.ModuleId, "1.0.0", "Host probe", "Test",
                LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, typeof(PackageHostProbeModule).FullName!, "1.1.0",
                [new LogicAssetRequirement("well", 1, 1, null, [])], [], []);
            var valid = await PackAsync(manifest);
            var requests = new List<string>();
            var host = new DotNetModulePackageHost(new CopySource(async (hash, stream, ct) =>
            {
                requests.Add(hash);
                await stream.WriteAsync(valid.Bytes, ct);
            }), root);
            var first = await host.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest);
            var second = await host.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest);
            Assert(requests.SequenceEqual(new[] { valid.Hash, valid.Hash }), "Host did not request exact immutable names.");
            var context = new LogicModuleExecutionContext(Guid.NewGuid(), first.Module.Identity, "package-host",
                DateTimeOffset.UtcNow, "{}", [], []);
            var firstResult = await first.Module.ExecuteAsync(context);
            var secondResult = await second.Module.ExecuteAsync(context);
            Assert(firstResult.StateUpdates["instance"] != secondResult.StateUpdates["instance"], "Runs shared a module instance.");
            await first.DisposeAsync();
            await first.DisposeAsync();
            await second.DisposeAsync();
            Assert(!Directory.EnumerateFileSystemEntries(root).Any(), "Disposed host left working files.");
            var beforeRejected = File.ReadAllText(marker);
            foreach (var candidate in new[]
            {
                (await PackAsync(manifest with { MinimumSdkVersion = "9999.0.0" }), "MODULE_SDK_INCOMPATIBLE"),
                (await PackAsync(manifest with { Runtime = LogicRuntimeKind.Python }), "MODULE_RUNTIME_INCOMPATIBLE"),
                (await PackAsync(manifest, "payload/../escaped.dll"), "MODULE_PACKAGE_INVALID")
            })
            {
                var candidateHost = new DotNetModulePackageHost(Bytes(candidate.Item1.Bytes), root);
                await RejectAsync(() => candidateHost.LoadAsync(manifest.ModuleId, manifest.Version,
                    candidate.Item1.Hash, candidate.Item1.Manifest), candidate.Item2);
            }
            await RejectAsync(() => host.LoadAsync("other-module", manifest.Version, valid.Hash, valid.Manifest), "MODULE_PACKAGE_IDENTITY_MISMATCH");
            await RejectAsync(() => host.LoadAsync(manifest.ModuleId, manifest.Version, new string('0', 64), valid.Manifest), "MODULE_PACKAGE_HASH_MISMATCH");
            await RejectAsync(() => host.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest + " "), "MODULE_PACKAGE_MANIFEST_MISMATCH");
            Assert(File.ReadAllText(marker) == beforeRejected, "Rejected package executed a constructor.");

            var wrongIdentity = await PackAsync(manifest with { ModuleId = "other-module" });
            await RejectAsync(() => new DotNetModulePackageHost(Bytes(wrongIdentity.Bytes), root)
                .LoadAsync("other-module", manifest.Version, wrongIdentity.Hash, wrongIdentity.Manifest), "MODULE_PACKAGE_IDENTITY_MISMATCH");
            var noEntry = await PackAsync(manifest with { EntryPoint = "Missing.Type" });
            await RejectAsync(() => new DotNetModulePackageHost(Bytes(noEntry.Bytes), root)
                .LoadAsync(manifest.ModuleId, manifest.Version, noEntry.Hash, noEntry.Manifest), "MODULE_ENTRY_POINT_INVALID");
            Assert(!Directory.EnumerateFileSystemEntries(root).Any(), "Failed activation left working files.");

            var oversized = new DotNetModulePackageHost(new CopySource(async (_, stream, ct) =>
            {
                var chunk = new byte[1024 * 1024];
                for (var i = 0; i <= 16; i++) await stream.WriteAsync(chunk, ct);
            }), root);
            await RejectAsync(() => oversized.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest), "MODULE_PACKAGE_TOO_LARGE");

            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new DotNetModulePackageHost(new CopySource(async (_, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }), root).LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using (var independent = await host.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest)
                .WaitAsync(TimeSpan.FromSeconds(5)))
                Assert(independent.Module.Identity.ModuleId == manifest.ModuleId, "Another read blocked a valid load.");
            cancellation.Cancel();
            try { await waiting; throw new InvalidOperationException("Download ignored cancellation."); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Assert(!Directory.EnumerateFileSystemEntries(root).Any(), "Cancellation left working files.");
            Console.WriteLine("Verified .NET package host: exact hash, preflight-before-construction, identity, independent runs, bounds, cancellation and cleanup passed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WSA_PACKAGE_HOST_TEST_MARKER", null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            File.Delete(marker);
        }
    }

    private static async Task<(byte[] Bytes, string Hash, string Manifest)> PackAsync(LogicModuleManifest manifest, string? payloadPath = null)
    {
        var json = JsonSerializer.Serialize(manifest, Options);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) await writer.WriteAsync(json);
            await using var image = File.OpenRead(Assembly.GetExecutingAssembly().Location);
            await using var target = archive.CreateEntry(payloadPath ?? "payload/probe.dll").Open();
            await image.CopyToAsync(target);
        }
        // The serializer has the same canonical JSON shape; unsafe packages deliberately cannot be inspected here.
        return (buffer.ToArray(), Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())), json);
    }

    private static CopySource Bytes(byte[] bytes) => new(async (_, stream, ct) => await stream.WriteAsync(bytes, ct));
    private sealed class CopySource(Func<string, Stream, CancellationToken, Task> copy) : IModulePackageSource
    {
        public Task CopyToAsync(string sha256, Stream destination, CancellationToken cancellationToken) => copy(sha256, destination, cancellationToken);
    }
    private static async Task RejectAsync(Func<Task<LoadedLogicModule>> load, string code)
    {
        try { await using var unexpected = await load(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected host rejection: " + code);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class PackageHostProbeModule : ILogicModuleV1, IDisposable
{
    public const string ModuleId = "weatherford.package-host-probe";
    private readonly string _instance = Guid.NewGuid().ToString("N");
    public PackageHostProbeModule() => Mark("constructed");
    public LogicModuleIdentity Identity { get; } = new(ModuleId, "1.0.0");
    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new LogicModuleExecutionResult("PROBE_COMPLETED", [], [], [],
            new Dictionary<string, string> { ["instance"] = _instance }));
    public void Dispose() => Mark("disposed");
    private static void Mark(string message)
    {
        if (Environment.GetEnvironmentVariable("WSA_PACKAGE_HOST_TEST_MARKER") is { } path) File.AppendAllText(path, message + "\n");
    }
}
