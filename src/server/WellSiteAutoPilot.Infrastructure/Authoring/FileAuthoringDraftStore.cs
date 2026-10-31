using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Authoring;

namespace WellSiteAutoPilot.Infrastructure.Authoring;

/// <summary>Local proof-of-concept draft store: one folder per draft under a configurable root.</summary>
public sealed class FileAuthoringDraftStore : IAuthoringDraftStore, IDisposable
{
    private static readonly JsonSerializerOptions Options = new(AuthoringJson.Options)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileAuthoringDraftStore(AuthoringOptions options)
    {
        root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.StorePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WellSiteAutoPilot", "authoring")
            : options.StorePath);
    }

    public void Dispose() => gate.Dispose();

    private string DraftDirectory(Guid id) => Path.Combine(root, "drafts", id.ToString("N"));
    private string DraftFile(Guid id) => Path.Combine(DraftDirectory(id), "draft.json");

    public async Task<IReadOnlyList<AuthoringDraftRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.Combine(root, "drafts");
            if (!Directory.Exists(directory)) return [];
            var records = new List<AuthoringDraftRecord>();
            foreach (var folder in Directory.EnumerateDirectories(directory))
                if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var id) &&
                    await ReadAsync(id, cancellationToken) is { } record)
                    records.Add(record);
            return records;
        }
        finally { gate.Release(); }
    }

    public async Task<AuthoringDraftRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(id, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task CreateAsync(AuthoringDraftRecord record, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await WriteAsync(record, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<AuthoringDraftRecord?> UpdateAsync(
        Guid id, Func<AuthoringDraftRecord, AuthoringDraftRecord> mutate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadAsync(id, cancellationToken);
            if (current is null) return null;
            var next = mutate(current);
            if (!ReferenceEquals(next, current)) await WriteAsync(next, cancellationToken);
            return next;
        }
        finally { gate.Release(); }
    }

    public async Task SavePackageAsync(Guid draftId, Guid buildId, byte[] package, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = PackageFile(draftId, buildId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicWriteAsync(path, package, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<byte[]?> ReadPackageAsync(Guid draftId, Guid buildId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = PackageFile(draftId, buildId);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        }
        finally { gate.Release(); }
    }

    private string PackageFile(Guid draftId, Guid buildId) =>
        Path.Combine(DraftDirectory(draftId), "builds", buildId.ToString("N"), "package.wsamodule");

    private async Task<AuthoringDraftRecord?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = DraftFile(id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AuthoringDraftRecord>(stream, Options, cancellationToken);
    }

    private async Task WriteAsync(AuthoringDraftRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DraftDirectory(record.Id));
        await AtomicWriteAsync(DraftFile(record.Id), JsonSerializer.SerializeToUtf8Bytes(record, Options), cancellationToken);
    }

    private static async Task AtomicWriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}
