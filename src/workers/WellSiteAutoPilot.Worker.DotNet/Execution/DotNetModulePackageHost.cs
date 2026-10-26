using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

/// <summary>Hosts reviewed managed packages. This is not an arbitrary-code sandbox.</summary>
public sealed class DotNetModulePackageHost(IModulePackageSource source, string workingDirectory)
{
    public async Task<LoadedLogicModule> LoadAsync(string moduleId, string version,
        string sha256, string reviewedManifestJson, CancellationToken cancellationToken = default)
    {
        // Validate the requested name before using it as a transport key or path.
        if (sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
            throw Failure("MODULE_PACKAGE_EXPECTATION_INVALID");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = cancellationToken;
        using var archiveBytes = new BoundedPackageBuffer();
        try { await source.CopyToAsync(sha256.ToLowerInvariant(), archiveBytes, deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_DOWNLOAD_TIMEOUT", FailureKind.DependencyTimeout,
                "The module package could not be retrieved in time.", retryable: true);
        }
        archiveBytes.Position = 0;
        var inspected = await LogicModulePackageReader.ReadAsync(archiveBytes, token);
        var sdk = typeof(ILogicModuleV1).Assembly.GetName().Version!.ToString(3);
        LogicModulePackageCompatibility.Validate(inspected,
            new LogicModulePackageExpectation(moduleId, version, sha256, LogicRuntimeKind.DotNet, sdk));
        LogicModulePackageCompatibility.ValidateCatalog(inspected, moduleId, version, sha256, reviewedManifestJson);

        var directory = Path.Combine(Path.GetFullPath(workingDirectory), Guid.NewGuid().ToString("N"));
        PackageLoadContext? context = null;
        try
        {
            Directory.CreateDirectory(directory);
            archiveBytes.Position = 0;
            using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("payload/", StringComparison.Ordinal) && !entry.FullName.EndsWith('/')))
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.GetFullPath(Path.Combine(directory, entry.FullName["payload/".Length..]));
                    if (!target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw Failure("MODULE_PACKAGE_INVALID");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await input.CopyToAsync(output, token);
                }
            }
            context = new PackageLoadContext(directory);
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                // The SDK always comes from the host, even when publish included a copy.
                if (Path.GetFileNameWithoutExtension(path) == typeof(ILogicModuleV1).Assembly.GetName().Name) continue;
                Assembly assembly;
                try { assembly = context.LoadManagedAssembly(path); }
                catch (BadImageFormatException) { continue; }
                var type = assembly.GetType(inspected.Manifest.EntryPoint);
                if (type is null || type.IsAbstract || !typeof(ILogicModuleV1).IsAssignableFrom(type)) continue;
                if (Activator.CreateInstance(type) is not ILogicModuleV1 module) throw Failure("MODULE_ENTRY_POINT_INVALID");
                if (module.Identity != new LogicModuleIdentity(moduleId, version))
                {
                    if (module is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                    else if (module is IDisposable disposable) disposable.Dispose();
                    throw Failure("MODULE_PACKAGE_IDENTITY_MISMATCH");
                }
                return new LoadedLogicModule(module, context, directory);
            }
            throw Failure("MODULE_ENTRY_POINT_INVALID");
        }
        catch
        {
            context?.Unload();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static WellSiteAutoPilotException Failure(string code) =>
        new(code, FailureKind.Validation, "The reviewed module package could not be hosted.");

    // Load managed images from streams so Windows permits deterministic working-file cleanup.
    // V1 hosts managed root-level dependencies only. Native dependency execution is unsupported.
    private sealed class PackageLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        public Assembly LoadManagedAssembly(string path)
        {
            using var stream = File.OpenRead(path);
            return LoadFromStream(stream);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(ILogicModuleV1).Assembly.GetName().Name) return typeof(ILogicModuleV1).Assembly;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadManagedAssembly(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            throw Failure("MODULE_NATIVE_DEPENDENCY_UNSUPPORTED");
    }

    private sealed class BoundedPackageBuffer : MemoryStream
    {
        private void Check(int count)
        {
            if (Position + count > LogicModulePackageService.MaximumUploadBytes)
                throw Failure("MODULE_PACKAGE_TOO_LARGE");
        }

        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
        public override void SetLength(long value)
        {
            if (value > LogicModulePackageService.MaximumUploadBytes) throw Failure("MODULE_PACKAGE_TOO_LARGE");
            base.SetLength(value);
        }
    }
}

public sealed class LoadedLogicModule(ILogicModuleV1 module, AssemblyLoadContext context, string directory) : IAsyncDisposable
{
    private ILogicModuleV1? _module = module;
    public ILogicModuleV1 Module => _module ?? throw new ObjectDisposedException(nameof(LoadedLogicModule));

    public async ValueTask DisposeAsync()
    {
        var value = Interlocked.Exchange(ref _module, null);
        if (value is null) return;
        try
        {
            if (value is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (value is IDisposable disposable) disposable.Dispose();
        }
        finally
        {
            context.Unload();
            Directory.Delete(directory, recursive: true);
        }
    }
}
