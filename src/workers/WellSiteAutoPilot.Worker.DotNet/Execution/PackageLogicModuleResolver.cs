using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.ObjectStore;
using NATS.Net;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

/// <summary>POC: resolves built-in modules first, then loads approved .NET packages that the Server
/// published to the WSA_MODULES Object Store. Only approved packages are ever published there.</summary>
public sealed class PackageLogicModuleResolver(INatsConnection connection)
    : ILogicModuleResolver
{
    private const string BucketName = "WSA_MODULES";
    private readonly BuiltInLogicModuleResolver _builtIn = new();
    private readonly ConcurrentDictionary<string, ILogicModuleV1> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ILogicModuleV1 Resolve(string moduleId, string version)
    {
        try { return _builtIn.Resolve(moduleId, version); }
        catch (WellSiteAutoPilotException) { }

        var key = $"{moduleId.Trim()}@{version.Trim()}";
        lock (_gate)
        {
            if (_loaded.TryGetValue(key, out var cached)) return cached;
            var module = LoadAsync(moduleId.Trim(), version.Trim()).GetAwaiter().GetResult();
            _loaded[key] = module;
            return module;
        }
    }

    private async Task<ILogicModuleV1> LoadAsync(string moduleId, string version)
    {
        var store = await connection.CreateObjectStoreContext().GetObjectStoreAsync(BucketName);
        await foreach (var info in store.ListAsync())
        {
            if (info.Deleted || !info.Name.StartsWith("sha256/", StringComparison.Ordinal)) continue;
            using var buffer = new MemoryStream();
            await store.GetAsync(info.Name, buffer);
            var bytes = buffer.ToArray();
            if (!string.Equals("sha256/" + Convert.ToHexStringLower(SHA256.HashData(bytes)), info.Name, StringComparison.Ordinal))
                continue;
            if (TryLoad(bytes, moduleId, version, out var module)) return module!;
        }
        throw new WellSiteAutoPilotException("MODULE_NOT_AVAILABLE", FailureKind.DependencyUnavailable,
            $"Logic Module '{moduleId}' version '{version}' has not been delivered to this worker.");
    }

    private static bool TryLoad(byte[] package, string moduleId, string version, out ILogicModuleV1? module)
    {
        module = null;
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null) return false;
        using var manifest = JsonDocument.Parse(manifestEntry.Open());
        var root = manifest.RootElement;
        if (!string.Equals(root.GetProperty("moduleId").GetString(), moduleId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(root.GetProperty("version").GetString(), version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(root.GetProperty("runtime").GetString(), "DotNet", StringComparison.OrdinalIgnoreCase))
            return false;
        var entryPoint = root.GetProperty("entryPoint").GetString()!;

        var directory = Path.Combine(Path.GetTempPath(), "wsa-modules", Guid.NewGuid().ToString("N"));
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("payload/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
        {
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName["payload/".Length..]));
            if (!target.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }

        var context = new ModuleLoadContext(directory);
        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
        {
            Assembly assembly;
            try { assembly = context.LoadFromAssemblyPath(dll); }
            catch (BadImageFormatException) { continue; }
            var type = assembly.GetType(entryPoint);
            if (type is null || !typeof(ILogicModuleV1).IsAssignableFrom(type)) continue;
            module = (ILogicModuleV1)Activator.CreateInstance(type)!;
            Console.WriteLine($"Loaded Logic Module {moduleId} {version} from the package store.");
            return true;
        }
        return false;
    }

    // The SDK must come from the host so module and worker share the same interface types.
    private sealed class ModuleLoadContext(string directory) : AssemblyLoadContext(isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(ILogicModuleV1).Assembly.GetName().Name) return null;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
