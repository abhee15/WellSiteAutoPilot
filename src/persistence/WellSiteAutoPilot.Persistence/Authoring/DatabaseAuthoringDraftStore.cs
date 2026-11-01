using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Application.Authoring;

namespace WellSiteAutoPilot.Persistence.Authoring;

/// <summary>
/// Database-backed draft store. The record is kept as one JSON document so the draft contract can evolve without
/// a migration per field; every write bumps a concurrency token and read-modify-write retries on conflict.
/// </summary>
public sealed class DatabaseAuthoringDraftStore(IServiceScopeFactory scopes) : IAuthoringDraftStore
{
    private const int ConflictRetries = 8;

    private static readonly JsonSerializerOptions Options = new(AuthoringJson.Options)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<AuthoringDraftRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var json = await Context(scope).AuthoringDrafts.AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc).Select(x => x.RecordJson).ToListAsync(cancellationToken);
        return json.Select(Read).ToList();
    }

    public async Task<AuthoringDraftRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var json = await Context(scope).AuthoringDrafts.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.RecordJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : Read(json);
    }

    public async Task CreateAsync(AuthoringDraftRecord record, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = Context(scope);
        context.AuthoringDrafts.Add(new AuthoringDraftEntity
        {
            Id = record.Id, Revision = record.Revision, RecordJson = Write(record),
            CreatedAtUtc = record.CreatedAt, UpdatedAtUtc = record.UpdatedAt, Version = 1
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthoringDraftRecord?> UpdateAsync(
        Guid id, Func<AuthoringDraftRecord, AuthoringDraftRecord> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ConflictRetries; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = Context(scope);
            var entity = await context.AuthoringDrafts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity is null) return null;
            var current = Read(entity.RecordJson);
            var next = mutate(current);
            if (ReferenceEquals(next, current)) return current;
            entity.RecordJson = Write(next);
            entity.Revision = next.Revision;
            entity.UpdatedAtUtc = next.UpdatedAt;
            entity.Version++;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return next;
            }
            catch (DbUpdateConcurrencyException) { /* another writer won; re-read and apply the mutation again */ }
        }
        throw new InvalidOperationException("The authoring draft is being changed too quickly; try again.");
    }

    public async Task SavePackageAsync(Guid draftId, Guid buildId, byte[] package, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = Context(scope);
        var existing = await context.AuthoringPackages.SingleOrDefaultAsync(
            x => x.DraftId == draftId && x.BuildId == buildId, cancellationToken);
        if (existing is null) context.AuthoringPackages.Add(new AuthoringPackageEntity { DraftId = draftId, BuildId = buildId, Package = package });
        else existing.Package = package;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]?> ReadPackageAsync(Guid draftId, Guid buildId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await Context(scope).AuthoringPackages.AsNoTracking()
            .Where(x => x.DraftId == draftId && x.BuildId == buildId)
            .Select(x => x.Package).SingleOrDefaultAsync(cancellationToken);
    }

    private static WellSiteAutoPilotDbContext Context(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<WellSiteAutoPilotDbContext>();

    private static string Write(AuthoringDraftRecord record) => JsonSerializer.Serialize(record, Options);

    private static AuthoringDraftRecord Read(string json) =>
        JsonSerializer.Deserialize<AuthoringDraftRecord>(json, Options)
        ?? throw new InvalidOperationException("The stored authoring draft is unreadable.");
}
