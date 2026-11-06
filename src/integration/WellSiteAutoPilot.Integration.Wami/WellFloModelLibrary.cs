using System.Globalization;

namespace WellSiteAutoPilot.Integration.Wami;

/// <summary>
/// A folder of WellFlo model files (.wfl), keyed by the file's path relative to the folder. A logic module (or the
/// coordinator) asks for a model by id and gets the bytes with a pinned hash and the file's last-write time as revision.
/// Read-only; ids that leave the folder, are not .wfl, or are too large are refused. Stands in for a WellFlo model
/// store, which does not exist as a service yet.
/// </summary>
public sealed class WellFloModelLibrary : IModelArtifactSource
{
    private readonly string _root;

    public WellFloModelLibrary(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("The model library folder does not exist.");
    }

    /// <summary>Model ids (relative paths with forward slashes), optionally filtered by a case-insensitive name fragment.</summary>
    public IReadOnlyList<string> List(string? nameContains = null) =>
        Directory.EnumerateFiles(_root, "*.wfl", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path).Replace('\\', '/'))
            .Where(id => nameContains is null || id.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async ValueTask<ModelArtifact> GetCurrentModelAsync(string assetExternalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetExternalId) || !assetExternalId.EndsWith(".wfl", StringComparison.OrdinalIgnoreCase))
            throw new ModelSourceException("MODEL_NOT_FOUND", "A model id is a relative path to a .wfl file.");
        var full = Path.GetFullPath(Path.Combine(_root, assetExternalId));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ModelSourceException("MODEL_NOT_FOUND", "The model id is outside the library.");
        var info = new FileInfo(full);
        if (!info.Exists) throw new ModelSourceException("MODEL_NOT_FOUND", "No such model in the library.");
        if (info.Length is 0 or > ModelArtifact.MaximumBytes)
            throw new ModelSourceException("MODEL_SOURCE_INVALID", "The model file is empty or larger than 8 MiB.");

        var bytes = await File.ReadAllBytesAsync(full, cancellationToken);
        return new ModelArtifact(assetExternalId.Replace('\\', '/'), info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
            ForeSiteModelArtifactSource.WamiFormat, bytes);
    }
}
