using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>Produces the immutable artifact used for preview and submission.</summary>
public static class DeclarativeThresholdPackageBuilder
{
    private static readonly DateTimeOffset ZipEpoch = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<DeclarativeThresholdArtifact> BuildAsync(
        LogicModuleManifest manifest, ThresholdDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        manifest = manifest with { EntryPoint = DeclarativeThresholdModule.EntryPoint };
        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest, cancellationToken);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteAsync(archive, "manifest.json", manifest, cancellationToken);
            await WriteAsync(archive, DeclarativeThresholdModule.PayloadPath, definition, cancellationToken);
        }

        buffer.Position = 0;
        var inspected = await LogicModulePackageReader.ReadAsync(buffer, cancellationToken);
        return new(buffer.ToArray(), inspected);
    }

    private static async Task WriteAsync<T>(ZipArchive archive, string path, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = ZipEpoch;
        await using var destination = entry.Open();
        await JsonSerializer.SerializeAsync(destination, value, Options, cancellationToken);
    }
}

public sealed class DeclarativeThresholdArtifact
{
    internal DeclarativeThresholdArtifact(ReadOnlyMemory<byte> bytes, InspectedLogicModulePackage package)
    {
        Bytes = bytes;
        Package = package;
    }

    public ReadOnlyMemory<byte> Bytes { get; }
    public InspectedLogicModulePackage Package { get; }
}
