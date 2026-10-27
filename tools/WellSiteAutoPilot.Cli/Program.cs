using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly DateTimeOffset DeterministicZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 3 &&
            string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "validate", StringComparison.OrdinalIgnoreCase))
        {
            return await ValidateManifestAsync(args[2]);
        }

        if (args.Length == 5 &&
            string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "pack", StringComparison.OrdinalIgnoreCase))
        {
            return await PackModuleAsync(args[2], args[3], args[4]);
        }

        if (args.Length == 3 &&
            string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "verify", StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyPackageAsync(args[2]);
        }

        if (args.Length == 8 &&
            string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "preflight", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<LogicRuntimeKind>(args[6], ignoreCase: true, out var runtime) ||
                !Enum.IsDefined(runtime) || int.TryParse(args[6], out _))
            {
                Console.Error.WriteLine("Host runtime must be DotNet or Python.");
                return 2;
            }
            return await VerifyPackageAsync(args[2], new LogicModulePackageExpectation(
                args[3], args[4], args[5], runtime, args[7]));
        }

        if (args.Length >= 2 && args[0].Equals("foresite", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("probe", StringComparison.OrdinalIgnoreCase))
            return await WellSiteAutoPilot.Cli.Diagnostics.ForeSiteProbeCommand.RunAsync(args);

        WriteUsage();
        return args.Length == 0 ? 0 : 2;
    }

    private static async Task<int> ValidateManifestAsync(string path)
    {
        try
        {
            var manifest = await LoadManifestAsync(path);

            Console.WriteLine(
                $"Valid Logic Module manifest: {manifest.ModuleId} {manifest.Version}");
            return 0;
        }
        catch (Exception exception) when (
            exception is JsonException or
            WellSiteAutoPilotException or
            IOException or
            UnauthorizedAccessException)
        {
            WriteSafeError(exception);
            return 2;
        }
    }

    private static async Task<int> PackModuleAsync(
        string manifestPath,
        string payloadDirectory,
        string outputPath)
    {
        try
        {
            var manifestFullPath = Path.GetFullPath(manifestPath);
            var payloadFullPath = Path.GetFullPath(payloadDirectory);
            var outputFullPath = Path.GetFullPath(outputPath);

            var manifest = await LoadManifestAsync(manifestFullPath);

            if (!Directory.Exists(payloadFullPath))
            {
                Console.Error.WriteLine($"Payload directory was not found: {payloadFullPath}");
                return 2;
            }

            if (!string.Equals(
                    Path.GetExtension(outputFullPath),
                    ".wsamodule",
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Logic Module package output must use the .wsamodule extension.");
                return 2;
            }

            if (IsWithinDirectory(outputFullPath, payloadFullPath))
            {
                Console.Error.WriteLine(
                    "Logic Module package output cannot be placed inside the payload directory.");
                return 2;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputFullPath) ??
                Directory.GetCurrentDirectory());

            var temporaryPath = outputFullPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            try
            {
                await using (var stream = File.Create(temporaryPath))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           leaveOpen: false))
                {
                    await AddFileAsync(
                        archive,
                        manifestFullPath,
                        "manifest.json");

                    var payloadFiles = Directory
                        .EnumerateFiles(payloadFullPath, "*", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();

                    foreach (var payloadFile in payloadFiles)
                    {
                        var relative = Path
                            .GetRelativePath(payloadFullPath, payloadFile)
                            .Replace(Path.DirectorySeparatorChar, '/');

                        await AddFileAsync(
                            archive,
                            payloadFile,
                            $"payload/{relative}");
                    }
                }

                await using (var candidate = File.OpenRead(temporaryPath))
                {
                    _ = await LogicModulePackageReader.ReadAsync(candidate);
                }
                File.Move(temporaryPath, outputFullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var checksum = await ComputeSha256Async(outputFullPath);

            Console.WriteLine(
                $"Packed Logic Module: {manifest.ModuleId} {manifest.Version}");
            Console.WriteLine($"Package: {outputFullPath}");
            Console.WriteLine($"SHA256: {checksum}");
            return 0;
        }
        catch (Exception exception) when (
            exception is JsonException or
            WellSiteAutoPilotException or
            IOException or
            UnauthorizedAccessException)
        {
            WriteSafeError(exception);
            return 2;
        }
    }


    private static async Task<int> VerifyPackageAsync(
        string packagePath, LogicModulePackageExpectation? expected = null)
    {
        try
        {
            var fullPath = Path.GetFullPath(packagePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".wsamodule", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Logic Module package must use the .wsamodule extension.");
                return 2;
            }
            await using var source = File.OpenRead(fullPath);
            if (source.Length > LogicModulePackageReader.MaximumPackageBytes)
            {
                Console.Error.WriteLine("Logic Module package exceeds the maximum supported package size.");
                return 2;
            }
            var package = await LogicModulePackageReader.ReadAsync(source);
            if (expected is not null) LogicModulePackageCompatibility.Validate(package, expected);
            Console.WriteLine($"Valid Logic Module package: {package.Manifest.ModuleId} {package.Manifest.Version}");
            Console.WriteLine($"SHA256: {package.Sha256}");
            Console.WriteLine($"Payload files: {package.PayloadFileCount}");
            if (expected is not null) Console.WriteLine("Package matches the expected identity, checksum and host runtime/SDK.");
            return 0;
        }
        catch (Exception exception) when (
            exception is WellSiteAutoPilotException or IOException or UnauthorizedAccessException)
        {
            WriteSafeError(exception);
            return 2;
        }
    }


    private static async Task<LogicModuleManifest> LoadManifestAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Manifest file was not found: {fullPath}",
                fullPath);
        }

        await using var stream = File.OpenRead(fullPath);
        return await LogicModuleManifestReader.ReadAsync(stream);
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName)
    {
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicZipTimestamp;

        await using var source = File.OpenRead(sourcePath);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsWithinDirectory(
        string candidatePath,
        string directoryPath)
    {
        var directory = Path.TrimEndingDirectorySeparator(directoryPath) +
                        Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(
            directory,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static void WriteSafeError(Exception exception)
    {
        switch (exception)
        {
            case WellSiteAutoPilotException wellSiteException:
                Console.Error.WriteLine(
                    $"{wellSiteException.Code}: {wellSiteException.SafeDetail}");
                break;
            case JsonException:
                Console.Error.WriteLine("Manifest JSON is invalid.");
                break;
            default:
                Console.Error.WriteLine(exception.Message);
                break;
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("WellSite AutoPilot CLI");
        Console.WriteLine("  foresite probe <well-id> <new-report.json> [samples:2-12] [interval-seconds:1-30]");
        Console.WriteLine("  module validate <manifest.json>");
        Console.WriteLine("  module pack <manifest.json> <payload-directory> <output.wsamodule>");
        Console.WriteLine("  module verify <package.wsamodule>");
        Console.WriteLine("  module preflight <package.wsamodule> <module-id> <module-version> <sha256> <DotNet|Python> <host-sdk-version>");
    }
}
