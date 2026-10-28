using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
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
            // An orphan with the host ownership marker is removed; active leases
            // from another host instance and unowned directories are preserved.
            var orphanId = Guid.NewGuid().ToString("N");
            var orphan = Path.Combine(root, orphanId);
            var unowned = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(orphan); Directory.CreateDirectory(unowned);
            await File.WriteAllTextAsync(Path.Combine(orphan, "leftover.bin"), "interrupted execution");
            await File.WriteAllTextAsync(Path.Combine(root + ".leases", orphanId + ".lease"), "WSA-MODULE-WORK-V1");
            var restarted = new DotNetModulePackageHost(new CopySource(async (_, stream, ct) => await stream.WriteAsync(valid.Bytes, ct)), root);
            await using (var afterRestart = await restarted.LoadAsync(manifest.ModuleId, manifest.Version, valid.Hash, valid.Manifest))
            {
                Assert(!Directory.Exists(orphan) && Directory.Exists(unowned), "Recovery deleted unknown data or retained an owned orphan.");
                Assert(Directory.EnumerateDirectories(root).Count() == 4, "Recovery removed another active host's working directory.");
                _ = await first.Module.ExecuteAsync(new LogicModuleExecutionContext(Guid.NewGuid(), first.Module.Identity,
                    "still-active", DateTimeOffset.UtcNow, "{}", [], []));
            }
            Directory.Delete(unowned);

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
            var request = new ExecutionRequestedV2(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), manifest.ModuleId,
                manifest.Version, "Shadow", "{}", [new("well", Guid.NewGuid(), "{}")], [], DateTimeOffset.UtcNow);
            var hosted = new ExecutionRequestedV3(request, new(valid.Hash, valid.Manifest));
            var wire = new MessageEnvelope<ExecutionRequestedV3>(Guid.NewGuid(), Subjects.ExecutionRequestedV3, 3,
                DateTimeOffset.UtcNow, "hosted-probe", null, hosted);
            var decoded = ExecutionRequestReader.Read(Subjects.ExecutionRequestedV3, JsonSerializer.Serialize(wire, Options));
            Assert(decoded.Package?.Sha256 == valid.Hash && decoded.Envelope.Payload.ExecutionId == request.ExecutionId,
                "V3 decoding lost the expected package/execution identity.");
            using var client = new HttpClient();
            var engine = new ModuleExecutionEngine(new GatewayDataClient(client), new BuiltInLogicModuleResolver(), packageHost: host);
            var observed = await engine.ExecuteObservedAsync(hosted, "hosted-probe", request.RequestedAtUtc);
            Assert(observed.Result.OutcomeCode == "PROBE_COMPLETED", "V3 did not execute the expected package.");
            try
            {
                await engine.ExecuteObservedAsync(request, "no-downgrade", request.RequestedAtUtc);
                throw new InvalidOperationException("Unpinned V2 request reached a hosted module.");
            }
            catch (WellSiteAutoPilotException error) when (error.Code == "MODULE_NOT_AVAILABLE") { }
            foreach (var invalidWire in new[] { wire with { ContractVersion = 2 }, wire with { Payload = hosted with { Package = null! } } })
            {
                try
                {
                    ExecutionRequestReader.Read(Subjects.ExecutionRequestedV3, JsonSerializer.Serialize(invalidWire, Options));
                    throw new InvalidOperationException("Invalid hosted request was accepted.");
                }
                catch (WellSiteAutoPilotException error) when (error.Code == "EXECUTION_REQUEST_INVALID") { }
            }
            var beforeRejected = File.ReadAllText(marker);
            foreach (var candidate in new[]
            {
                (await PackAsync(manifest with { EntryPoint = typeof(StructuredHostProbeModule).FullName! }), "MODULE_CONTRACT_UNSUPPORTED"),
                (await PackAsync(manifest with
                {
                    ManifestVersion = 2, MinimumSdkVersion = "1.2.0",
                    DataRequirements = [new("value", "well", "Value", "Current", null, 60, false,
                        new("example.number/v1", JsonSerializer.SerializeToElement(new { type = "number" })), "structured.current")],
                    OutputRequirements = [new("value", "Value", null,
                        new("example.number/v1", JsonSerializer.SerializeToElement(new { type = "number" })))]
                }), "MODULE_CONTRACT_UNSUPPORTED"),
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
            if (Directory.Exists(root + ".leases")) Directory.Delete(root + ".leases", recursive: true);
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

// Even a dual-interface module cannot silently downgrade structured execution.
public sealed class StructuredHostProbeModule : ILogicModuleV1, ILogicModuleV2
{
    public StructuredHostProbeModule() => throw new InvalidOperationException("Unsupported constructor must never run.");
    public LogicModuleIdentity Identity => new("structured-probe", "1.0.0");
    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Unsupported contract.");
    public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Unsupported contract.");
}
