using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    public Task UpdateDraftAuthorizedAsync(ConfiguredLogicRevision revision, int expectedDraftVersion,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ValidateDraft(revision);
        if (expectedDraftVersion < 0 || revision.DraftVersion != (long)expectedDraftVersion + 1) throw Conflict();
        return WriteConfigurationAuthorizedAsync(subject, actor, async () =>
        {
            var definition = await LockConfigurationAsync(revision.ConfiguredLogicId, cancellationToken);
            var targets = definition.Revisions.SelectMany(DraftAssets)
                .Concat(DraftAssets(revision)).Distinct().Order().ToArray();
            // Retain authority over both the old and replacement targets. Removing
            // a binding must never grant access to an otherwise foreign configuration.
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            var current = definition.Revisions.SingleOrDefault(item => item.Id == revision.Id);
            if (current is null || current.Status != ConfiguredLogicRevisionStatus.Draft ||
                current.DraftVersion != expectedDraftVersion || current.RevisionNumber != revision.RevisionNumber ||
                current.CreatedAtUtc != revision.CreatedAtUtc || definition.ActiveRevisionId == revision.Id) throw Conflict();
            await RequireViewSnapshotAsync(revision, subject, cancellationToken, current);
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            var changed = await dbContext.ConfiguredLogicRevisions
                .Where(item => item.Id == revision.Id && item.Status == nameof(ConfiguredLogicRevisionStatus.Draft) &&
                    item.DraftVersion == expectedDraftVersion)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.DraftVersion, revision.DraftVersion)
                    .SetProperty(item => item.ModuleId, revision.ModuleId)
                    .SetProperty(item => item.ModuleVersion, revision.ModuleVersion)
                    .SetProperty(item => item.ModuleManifestJson, revision.ModuleManifestJson)
                    .SetProperty(item => item.Mode, revision.Mode.ToString())
                    .SetProperty(item => item.ParametersJson, revision.ParametersJson)
                    .SetProperty(item => item.ScheduleJson, revision.Schedule == null ? null : JsonSerializer.Serialize(revision.Schedule))
                    .SetProperty(item => item.ViewId, revision.View == null ? null : (Guid?)revision.View.ViewId)
                    .SetProperty(item => item.ViewRevision, revision.View == null ? null : (int?)revision.View.Revision)
                    .SetProperty(item => item.ViewConnectionId, revision.View == null ? null : (Guid?)revision.View.ConnectionId)
                    .SetProperty(item => item.ViewRole, revision.View == null ? null : revision.View.Role), cancellationToken);
            if (changed != 1) throw Conflict();
            await dbContext.ConfiguredLogicAssetBindings.Where(item => item.RevisionId == revision.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.ConfiguredLogicDataBindings.Where(item => item.RevisionId == revision.Id).ExecuteDeleteAsync(cancellationToken);
            // Internal service callers may reuse a context that created this draft.
            foreach (var entry in dbContext.ChangeTracker.Entries<ConfiguredLogicAssetBindingEntity>()
                         .Where(entry => entry.Entity.RevisionId == revision.Id).ToArray()) entry.State = EntityState.Detached;
            foreach (var entry in dbContext.ChangeTracker.Entries<ConfiguredLogicDataBindingEntity>()
                         .Where(entry => entry.Entity.RevisionId == revision.Id).ToArray()) entry.State = EntityState.Detached;
            AddBindings(revision);
            Audit(revision, "CONFIGURED_LOGIC_DRAFT_UPDATED", actor, (clock ?? TimeProvider.System).GetUtcNow());
        }, cancellationToken);
    }
}
