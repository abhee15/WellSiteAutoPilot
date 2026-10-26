using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

internal static class PackageChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task RunAsync(LogicModuleManifest manifest)
    {
        var bytes = Archive(JsonSerializer.Serialize(manifest, Options));
        using var source = new MemoryStream(bytes);
        var inspected = await LogicModulePackageReader.ReadAsync(source);
        Assert(inspected.Sha256 == Convert.ToHexStringLower(SHA256.HashData(bytes)), "Package hash must cover original archive bytes.");
        Assert(inspected.Manifest.ModuleId == manifest.ModuleId && inspected.Manifest.Version == manifest.Version, "Manifest identity was lost.");
        Assert(inspected.PayloadFileCount == 1 && source.CanRead, "Inspection must retain source ownership and payload count.");
        var optionalFieldsOmitted = JsonSerializer.Serialize(manifest, Options)
            .Replace("\"requiredAssetTypeKey\":\"well\",", "", StringComparison.Ordinal)
            .Replace("\"canonicalUnit\":\"%\",", "", StringComparison.Ordinal)
            .Replace("\"canonicalUnit\":\"spm\"", "\"canonicalUnit\":null", StringComparison.Ordinal);
        using var optionalSource = new MemoryStream(Archive(optionalFieldsOmitted));
        _ = await LogicModulePackageReader.ReadAsync(optionalSource);
        LogicModulePackageCompatibility.ValidateCatalog(inspected, manifest.ModuleId, manifest.Version, inspected.Sha256, inspected.ManifestJson);
        AssertFailure(() => LogicModulePackageCompatibility.ValidateCatalog(inspected, manifest.ModuleId, manifest.Version,
            inspected.Sha256, inspected.ManifestJson + " "), "MODULE_PACKAGE_MANIFEST_MISMATCH");
        var expected = new LogicModulePackageExpectation(manifest.ModuleId, manifest.Version,
            inspected.Sha256, manifest.Runtime, "1.1.0");
        LogicModulePackageCompatibility.Validate(inspected, expected);
        LogicModulePackageCompatibility.Validate(inspected, expected with { PackageSha256 = inspected.Sha256.ToUpperInvariant() });
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { PackageSha256 = "abc" }), "MODULE_PACKAGE_EXPECTATION_INVALID");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { PackageSha256 = new string('0', 64) }), "MODULE_PACKAGE_HASH_MISMATCH");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { ModuleId = "different" }), "MODULE_PACKAGE_IDENTITY_MISMATCH");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { ModuleVersion = "9.0.0" }), "MODULE_PACKAGE_IDENTITY_MISMATCH");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { Runtime = LogicRuntimeKind.DotNet }), "MODULE_RUNTIME_INCOMPATIBLE");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(inspected, expected with { SdkVersion = "one" }), "MODULE_HOST_SDK_VERSION_INVALID");
        var sdk11 = inspected with { Manifest = manifest with { MinimumSdkVersion = "1.1.0" } };
        AssertFailure(() => LogicModulePackageCompatibility.Validate(sdk11, expected with { SdkVersion = "1.0.0" }), "MODULE_SDK_INCOMPATIBLE");
        AssertFailure(() => LogicModulePackageCompatibility.Validate(sdk11, expected with { SdkVersion = "1.1.0-rc.1" }), "MODULE_SDK_INCOMPATIBLE");
        LogicModulePackageCompatibility.Validate(sdk11, expected with { SdkVersion = "1.1.0+host.build" });

        // SemVer's normative precedence example, plus very large numeric identifiers.
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
            "999999999999999999999999.0.0"];
        for (var minimum = 0; minimum < ordered.Length; minimum++)
        for (var host = 0; host < ordered.Length; host++)
        {
            var package = inspected with { Manifest = manifest with { MinimumSdkVersion = ordered[minimum] } };
            var target = expected with { SdkVersion = ordered[host] };
            if (host >= minimum) LogicModulePackageCompatibility.Validate(package, target);
            else AssertFailure(() => LogicModulePackageCompatibility.Validate(package, target), "MODULE_SDK_INCOMPATIBLE");
        }
        foreach (var version in new[] { "1.0", "01.0.0", "1.0.0-01", "1.0.0-a..b", "1.0.0+", "1.0.0\n" })
        {
            AssertFailure(() => LogicModuleManifestValidator.Validate(manifest with { Version = version }), "MODULE_VERSION_INVALID");
            AssertFailure(() => LogicModuleManifestValidator.Validate(manifest with { MinimumSdkVersion = version }), "MODULE_SDK_VERSION_INVALID");
        }

        foreach (var name in new[] { "../outside", "/absolute", "payload/../outside", "payload\\module.py",
            "payload/C:drive", "payload/a//b", "payload/a//", "payload/NUL.txt", "payload/trailing.",
            "payload/trailing ", "payload/wild*card", "elsewhere/file.py" })
        {
            await RejectAsync(Archive(JsonSerializer.Serialize(manifest, Options), name));
        }
        await RejectAsync(Archive("null"));
        await RejectAsync(Archive("{}"));
        await RejectAsync(Archive("{"));
        await RejectAsync(Archive(JsonSerializer.Serialize(manifest, Options), "payload/code.py", "payload/CODE.py"));
        await RejectAsync(Archive(JsonSerializer.Serialize(manifest, Options), "payload/code", "payload/code/file.py"));
        await RejectAsync(Archive(JsonSerializer.Serialize(manifest, Options), includePayload: false));
        await RejectAsync(Archive(JsonSerializer.Serialize(manifest, Options), symlink: true));
        await RejectAsync(Encoding.UTF8.GetBytes("not a ZIP archive"));
        await RejectAsync(Archive(new string(' ', 1024 * 1024 + 1)));
        foreach (var badManifest in new[]
        {
            manifest with { AssetRequirements = [null!] },
            manifest with { DataRequirements = [null!] },
            manifest with { CommandRequirements = [null!] },
            manifest with { AssetRequirements = null! },
            manifest with { AssetRequirements = [manifest.AssetRequirements.First() with { RequiredTraits = null! }] },
            manifest with { Runtime = (LogicRuntimeKind)99 }
        })
        {
            using var malformed = new MemoryStream(Archive(JsonSerializer.Serialize(badManifest, Options)));
            try { await LogicModulePackageReader.ReadAsync(malformed); }
            catch (WellSiteAutoPilotException) { continue; }
            throw new InvalidOperationException("Malformed manifest was accepted.");
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var canceledSource = new MemoryStream(bytes);
        try { await LogicModulePackageReader.ReadAsync(canceledSource, canceled.Token); }
        catch (OperationCanceledException) { Console.WriteLine("Package inspection and host compatibility checks passed."); return; }
        throw new InvalidOperationException("Cancellation was ignored.");
    }

    private static byte[] Archive(string manifest, string payload = "payload/code.py", string? extra = null,
        bool includePayload = true, bool symlink = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive.CreateEntry("manifest.json"), manifest);
            if (includePayload)
            {
                var entry = archive.CreateEntry(payload);
                if (symlink) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                Write(entry, "arbitrary code is inspected, never loaded");
            }
            if (extra is not null) Write(archive.CreateEntry(extra), "extra");
        }
        return stream.ToArray();
    }

    private static void Write(ZipArchiveEntry entry, string content)
    {
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static async Task RejectAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        try { await LogicModulePackageReader.ReadAsync(stream); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_PACKAGE_INVALID") { return; }
        throw new InvalidOperationException("Malformed archive was accepted.");
    }

    private static void AssertFailure(Action action, string code)
    {
        try { action(); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected {code}.");
    }

    private static void Assert(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
