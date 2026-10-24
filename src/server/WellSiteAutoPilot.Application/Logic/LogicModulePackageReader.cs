using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public sealed record InspectedLogicModulePackage(
    LogicModuleManifest Manifest, string Sha256, int PayloadFileCount);

/// <summary>
/// Inspects a bounded snapshot without extracting files or loading module code.
/// A successful inspection establishes structure/integrity only, never approval.
/// </summary>
public static class LogicModulePackageReader
{
    public const long MaximumPackageBytes = 256L * 1024L * 1024L;
    private const long MaximumExpandedBytes = 512L * 1024L * 1024L;
    private const long MaximumManifestBytes = 1024L * 1024L;
    private const int MaximumEntries = 10_000;
    public static async Task<InspectedLogicModulePackage> ReadAsync(
        Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            // Hash and inspect the same snapshot even if a caller's file changes later.
            using var snapshot = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (snapshot.Length + count > MaximumPackageBytes)
                    Fail("The package exceeds the maximum supported size.");
                await snapshot.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            snapshot.Position = 0;
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(snapshot, cancellationToken));
            snapshot.Position = 0;
            using var archive = new ZipArchive(snapshot, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaximumEntries) Fail("The package contains too many archive entries.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            ZipArchiveEntry? manifestEntry = null;
            var payloadCount = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName;
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                    (name.EndsWith('/') && entry.Length != 0))
                    Fail("The package contains an unsupported link or directory entry.");
                if (!IsSafeName(name) || !names.Add(name)) Fail("The package contains an unsafe or duplicate archive entry.");
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes) Fail("The package exceeds the maximum expanded size.");
                if (name == "manifest.json") manifestEntry = entry;
                else if (!name.StartsWith("payload/", StringComparison.Ordinal)) Fail("The package contains an entry outside its payload.");
                else if (!name.EndsWith('/')) payloadCount++;
                // Reject file/directory aliases before any future extractor sees them.
                var path = name.TrimEnd('/');
                if (name.EndsWith('/')) directories.Add(path);
                else files.Add(path);
                for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
                {
                    directories.Add(path[..slash]);
                    if (slash == 0) break;
                }
            }
            if (files.Overlaps(directories)) Fail("The package contains a file/directory path conflict.");
            if (manifestEntry is null || manifestEntry.Length > MaximumManifestBytes)
                Fail("The package must contain a bounded manifest.json.");
            if (payloadCount == 0) Fail("The package does not contain a payload.");
            // Read the payload too: central-directory sizes alone do not prove
            // that the compressed bytes exist or decode within the declared bound.
            foreach (var entry in archive.Entries)
            {
                await using var content = entry.Open();
                long read = 0;
                while ((count = await content.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    read += count;
                    if (read > entry.Length) Fail("An archive entry exceeds its declared size.");
                }
                if (read != entry.Length) Fail("An archive entry does not match its declared size.");
            }
            await using var manifestStream = manifestEntry!.Open();
            var manifest = await LogicModuleManifestReader.ReadAsync(manifestStream, cancellationToken);
            return new InspectedLogicModulePackage(manifest, sha256, payloadCount);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or OverflowException)
        {
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_INVALID", FailureKind.Validation,
                "The module package archive or manifest is malformed.", innerException: exception);
        }
    }

    private static bool IsSafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.EndsWith("//", StringComparison.Ordinal)) return false;
        var segments = name.TrimEnd('/').Split('/');
        return segments.All(part => part.Length != 0 && part is not "." and not ".." &&
            !part.EndsWith('.') && !part.EndsWith(' ') &&
            !part.Any(character => character < 32 || ":*?\"<>|".Contains(character)) &&
            !IsWindowsDeviceName(part));
    }

    private static bool IsWindowsDeviceName(string part)
    {
        var stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string detail) =>
        throw new WellSiteAutoPilotException("MODULE_PACKAGE_INVALID", FailureKind.Validation, detail);
}
