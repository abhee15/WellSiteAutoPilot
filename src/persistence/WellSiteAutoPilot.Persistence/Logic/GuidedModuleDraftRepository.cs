using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class GuidedModuleDraftRepository(WellSiteAutoPilotDbContext db, TimeProvider clock) : IGuidedModuleDraftRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<GuidedModuleDraft?> FindOwnedAsync(Guid id, AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var entity = await OwnedDrafts(subject).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity is null ? null : ToDraft(entity);
    }

    public async Task<IReadOnlyList<GuidedModuleDraft>> ListOwnedAsync(AccessSubject subject, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        var entities = await OwnedDrafts(subject).OrderBy(item => item.Id).Skip(offset).Take(limit).ToArrayAsync(cancellationToken);
        return entities.Select(ToDraft).ToArray();
    }

    private IQueryable<GuidedModuleDraftEntity> OwnedDrafts(AccessSubject subject)
    {
        var authorized = ScopedAccessQueries.DeploymentGrants(db, subject.UserId, WellSitePermissions.GuidedDraftManage,
            subject.Groups, clock.GetUtcNow());
        return db.GuidedModuleDrafts.AsNoTracking().Where(item => item.OwnerUserId == subject.UserId && authorized.Any());
    }

    private static GuidedModuleDraft ToDraft(GuidedModuleDraftEntity entity) =>
        new(entity.Id, entity.OwnerUserId, entity.Revision, JsonSerializer.Deserialize<GuidedModuleDraftContent>(entity.ContentJson, Json)!);

    public Task AddOwnedAsync(GuidedModuleDraft draft, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default) =>
        WriteAsync(draft, subject, actor, "guided-draft.created", () =>
        {
            if (draft.Id == Guid.Empty || draft.Revision != 1) throw Conflict();
            db.GuidedModuleDrafts.Add(new()
            {
                Id = draft.Id, OwnerUserId = draft.OwnerUserId, Revision = draft.Revision,
                ContentJson = JsonSerializer.Serialize(draft.Content, Json)
            });
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UpdateOwnedAsync(GuidedModuleDraft draft, int expectedRevision, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default) =>
        WriteAsync(draft, subject, actor, "guided-draft.edited", async () =>
        {
            if (expectedRevision < 1 || draft.Revision != (long)expectedRevision + 1) throw Conflict();
            var content = JsonSerializer.Serialize(draft.Content, Json);
            var changed = await db.GuidedModuleDrafts
                .Where(item => item.Id == draft.Id && item.OwnerUserId == subject.UserId && item.Revision == expectedRevision)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Revision, draft.Revision)
                    .SetProperty(item => item.ContentJson, content), cancellationToken);
            if (changed != 1) throw Conflict();
        }, cancellationToken);

    public async Task<IReadOnlyList<GuidedModuleReviewItem>> ListPendingSubmissionsAsync(AccessSubject subject, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        var reviewed = db.LogicModules.Where(module => module.TrustStatus != nameof(LogicModuleTrustStatus.Untrusted));
        // The immutable package supplies the original name, including for existing submissions.
        // Filter and page before fetching the bounded guided packages. Evidence stays on the detail path.
        var page = await ReviewableSubmissions(subject)
            .Where(item => !reviewed.Any(module => module.PackageSha256 == item.PackageSha256))
            .OrderByDescending(item => item.SubmittedAtUtc).ThenBy(item => item.DraftId).ThenByDescending(item => item.Revision)
            .Skip(offset).Take(limit)
            .Select(item => new { item.DraftId, item.Revision, item.SubmittedAtUtc, item.PackageSha256, item.PackageBytes })
            .ToArrayAsync(cancellationToken);
        var items = new List<GuidedModuleReviewItem>(page.Length);
        foreach (var item in page)
        {
            var package = await InspectSubmissionAsync(item.PackageBytes, item.PackageSha256, cancellationToken);
            items.Add(new(item.DraftId, item.Revision, package.Manifest.DisplayName, item.SubmittedAtUtc));
        }
        return items;
    }

    private IQueryable<GuidedModuleSubmissionEntity> ReviewableSubmissions(AccessSubject subject)
    {
        var authorized = ScopedAccessQueries.DeploymentGrants(db, subject.UserId, WellSitePermissions.GuidedDraftReview,
            subject.Groups, clock.GetUtcNow());
        return db.GuidedModuleSubmissions.AsNoTracking().Where(_ => authorized.Any());
    }

    private static async Task<InspectedLogicModulePackage> InspectSubmissionAsync(byte[] bytes, string packageSha256,
        CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(bytes);
        var inspected = await LogicModulePackageReader.ReadAsync(source, cancellationToken);
        if (inspected.Sha256 != packageSha256 || inspected.DeclarativeModule is null)
            throw new WellSiteAutoPilotException("GUIDED_SUBMISSION_INVALID", FailureKind.Conflict, "The reviewed package could not be verified.");
        return inspected;
    }

    public async Task<GuidedModuleReviewSnapshot?> FindSubmissionAsync(Guid id, int revision, AccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        var entity = await ReviewableSubmissions(subject)
            .SingleOrDefaultAsync(item => item.DraftId == id && item.Revision == revision, cancellationToken);
        if (entity is null) return null;
        var inspected = await InspectSubmissionAsync(entity.PackageBytes, entity.PackageSha256, cancellationToken);
        return new(id, revision, entity.PackageSha256, entity.SubmittedAtUtc, entity.PackageBytes, inspected.ManifestJson,
            inspected.DeclarativeModule!.Definition,
            JsonSerializer.Deserialize<DeclarativeThresholdEvidence[]>(entity.EvidenceJson, Json)!,
            inspected.Manifest.EntryPoint == DeclarativeThresholdModule.DifferenceEntryPoint);
    }

    public async Task<GuidedModuleSubmissionReceipt> SubmitOwnedAsync(GuidedModuleDraft draft, GuidedModuleSubmission submission,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        if (submission.DraftId != draft.Id || submission.DraftRevision != draft.Revision) throw Conflict();
        GuidedModuleSubmissionReceipt? receipt = null;
        await WriteAsync(draft, subject, actor, "guided-draft.submitted", async () =>
        {
            // Lock the same row that edit updates, then compare the saved revision.
            var current = await db.GuidedModuleDrafts.FromSqlInterpolated($"""
                SELECT * FROM logic.guided_module_drafts
                 WHERE "Id" = {draft.Id} AND "OwnerUserId" = {subject.UserId}
                 FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (current is null || current.Revision != draft.Revision) throw Conflict();

            var existing = await db.GuidedModuleSubmissions.AsNoTracking()
                .SingleOrDefaultAsync(item => item.DraftId == draft.Id && item.Revision == draft.Revision, cancellationToken);
            if (existing is not null)
                throw new WellSiteAutoPilotException("GUIDED_DRAFT_ALREADY_SUBMITTED", FailureKind.Conflict,
                    "This draft revision was already submitted.");

            var now = clock.GetUtcNow();
            var hash = submission.Artifact.Package.Sha256;
            db.GuidedModuleSubmissions.Add(new()
            {
                DraftId = draft.Id, Revision = draft.Revision, PackageSha256 = hash,
                PackageBytes = submission.Artifact.Bytes.ToArray(),
                EvidenceJson = JsonSerializer.Serialize(submission.Evidence, Json), SubmittedAtUtc = now
            });
            receipt = new(draft.Id, draft.Revision, hash, now);
        }, cancellationToken);
        return receipt!;
    }

    private async Task WriteAsync(GuidedModuleDraft draft, AccessSubject subject, SecurityActorContext actor,
        string action, Func<Task> write, CancellationToken cancellationToken)
    {
        if (subject.UserId == Guid.Empty || actor.UserId != subject.UserId || draft.OwnerUserId != subject.UserId)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_ACCESS_DENIED", FailureKind.Authorization, "Draft owner access is required.");

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(db, subject, WellSitePermissions.GuidedDraftManage, clock, cancellationToken);
            await write();
            db.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = clock.GetUtcNow(), ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName, CorrelationId = actor.CorrelationId,
                Action = action, TargetType = "guided-draft", TargetId = draft.Id.ToString("D"),
                DetailsJson = JsonSerializer.Serialize(new { draft.Revision })
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error) when (error.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_REVISION_CONFLICT", FailureKind.Conflict,
                "The draft or its authority changed. Reload it before retrying.", innerException: error);
        }
    }

    private static WellSiteAutoPilotException Conflict() =>
        new("GUIDED_DRAFT_REVISION_CONFLICT", FailureKind.Conflict, "The guided draft changed. Reload it before retrying.");
}
