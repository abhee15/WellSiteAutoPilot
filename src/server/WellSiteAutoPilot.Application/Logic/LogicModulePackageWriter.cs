using System.IO.Compression;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Writes the deterministic .wsamodule archive layout shared by the CLI and the authoring build.
/// The caller still inspects the result with <see cref="LogicModulePackageReader"/>.
/// </summary>
public static class LogicModulePackageWriter
{
    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task WriteAsync(Stream destination, string manifestPath, string payloadDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        await AddFileAsync(archive, manifestPath, "manifest.json", cancellationToken);
        var payloadFiles = Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var payloadFile in payloadFiles)
        {
            var relative = Path.GetRelativePath(payloadDirectory, payloadFile).Replace(Path.DirectorySeparatorChar, '/');
            await AddFileAsync(archive, payloadFile, $"payload/{relative}", cancellationToken);
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicZipTimestamp;
        await using var source = File.OpenRead(sourcePath);
        await using var target = entry.Open();
        await source.CopyToAsync(target, cancellationToken);
    }
}
