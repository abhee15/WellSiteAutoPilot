using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

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

        WriteUsage();
        return args.Length == 0 ? 0 : 2;
    }

    private static async Task<int> ValidateManifestAsync(string path)
    {
        try
        {
            var manifest = await LoadManifestAsync(path);
            LogicModuleManifestValidator.Validate(manifest);

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
            LogicModuleManifestValidator.Validate(manifest);

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


    private static async Task<int> VerifyPackageAsync(string packagePath)
    {
        const long maximumPackageBytes = 256L * 1024L * 1024L;
        const long maximumExpandedBytes = 512L * 1024L * 1024L;
        const long maximumManifestBytes = 1024L * 1024L;
        const int maximumEntries = 10_000;

        try
        {
            var fullPath = Path.GetFullPath(packagePath);

            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Logic Module package was not found: {fullPath}");
                return 2;
            }

            if (!string.Equals(
                    Path.GetExtension(fullPath),
                    ".wsamodule",
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Logic Module package must use the .wsamodule extension.");
                return 2;
            }

            var packageInfo = new FileInfo(fullPath);
            if (packageInfo.Length > maximumPackageBytes)
            {
                Console.Error.WriteLine("Logic Module package exceeds the maximum supported package size.");
                return 2;
            }

            await using var stream = File.OpenRead(fullPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count > maximumEntries)
            {
                Console.Error.WriteLine("Logic Module package contains too many archive entries.");
                return 2;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            ZipArchiveEntry? manifestEntry = null;
            var payloadFileCount = 0;

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');

                if (!IsSafeArchiveEntry(name))
                {
                    Console.Error.WriteLine($"Logic Module package contains an unsafe archive entry: {entry.FullName}");
                    return 2;
                }

                if (!names.Add(name))
                {
                    Console.Error.WriteLine($"Logic Module package contains a duplicate archive entry: {entry.FullName}");
                    return 2;
                }

                checked
                {
                    expandedBytes += entry.Length;
                }

                if (expandedBytes > maximumExpandedBytes)
                {
                    Console.Error.WriteLine("Logic Module package exceeds the maximum expanded size.");
                    return 2;
                }

                if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestEntry = entry;
                }
                else if (name.StartsWith("payload/", StringComparison.Ordinal) &&
                         !name.EndsWith("/", StringComparison.Ordinal))
                {
                    payloadFileCount++;
                }
                else if (!name.EndsWith("/", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        $"Logic Module package contains a file outside the payload directory: {entry.FullName}");
                    return 2;
                }
            }

            if (manifestEntry is null)
            {
                Console.Error.WriteLine("Logic Module package does not contain manifest.json.");
                return 2;
            }

            if (manifestEntry.Length > maximumManifestBytes)
            {
                Console.Error.WriteLine("Logic Module manifest exceeds the maximum supported size.");
                return 2;
            }

            if (payloadFileCount == 0)
            {
                Console.Error.WriteLine("Logic Module package does not contain a payload.");
                return 2;
            }

            LogicModuleManifest manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<LogicModuleManifest>(
                               manifestStream,
                               SerializerOptions) ??
                           throw new JsonException(
                               "Logic Module package manifest is empty.");
            }

            LogicModuleManifestValidator.Validate(manifest);
            var checksum = await ComputeSha256Async(fullPath);

            Console.WriteLine($"Valid Logic Module package: {manifest.ModuleId} {manifest.Version}");
            Console.WriteLine($"SHA256: {checksum}");
            Console.WriteLine($"Payload files: {payloadFileCount}");
            return 0;
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidDataException or
            OverflowException or
            WellSiteAutoPilotException or
            IOException or
            UnauthorizedAccessException)
        {
            WriteSafeError(exception);
            return 2;
        }
    }

    private static bool IsSafeArchiveEntry(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            entryName.StartsWith("/", StringComparison.Ordinal) ||
            entryName.Contains(':'))
        {
            return false;
        }

        var segments = entryName.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 &&
               segments.All(segment =>
                   segment is not "." and not "..");
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
        return await JsonSerializer.DeserializeAsync<LogicModuleManifest>(
                   stream,
                   SerializerOptions) ??
               throw new JsonException(
                   "Manifest file did not contain a Logic Module manifest.");
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
        Console.WriteLine("  module validate <manifest.json>");
        Console.WriteLine("  module pack <manifest.json> <payload-directory> <output.wsamodule>");
        Console.WriteLine("  module verify <package.wsamodule>");
    }
}
