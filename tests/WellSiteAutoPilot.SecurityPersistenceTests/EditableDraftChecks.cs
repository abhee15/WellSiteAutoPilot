using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;

internal static class EditableDraftChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessSubject subject, SecurityActorContext originalActor, string manifest, Guid asset, Guid outsideAsset)
    {
        var actor = originalActor with { CorrelationId = "editable-draft-probe" };
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var service = Service(setup);
        var command = new CreateConfiguredLogicRevisionCommand(manifest, "{}", [new("target", asset, "{}")], [], null);
        var definition = await service.CreateAuthorizedAsync(new("Editable draft probe", manifest, "{}", command.AssetBindings, [], null), subject, actor);
        var active = definition.Revisions.Single();
        await service.ValidateRevisionAuthorizedAsync(definition.Id, active.Id, subject, actor);
        await service.ActivateRevisionAuthorizedAsync(definition.Id, active.Id, "none", subject, actor);
        var createdDraft = await service.CreateRevisionAuthorizedAsync(definition.Id, command, subject, actor);
        // PostgreSQL persists timestamps at microsecond precision; compare the saved identity.
        var draft = (await new ConfiguredLogicRepository(setup).GetAsync(definition.Id))!.Revisions.Single(item => item.Id == createdDraft.Id);
        var schedule = new ConfiguredLogicScheduleCommand(true, 300, DateTimeOffset.UtcNow, "UTC");
        var edit = command with { ParametersJson = "{\"nested\":{\"values\":[1,2,3]}}", Schedule = schedule,
            AssetBindings = [new("target", asset, "{\"override\":true}")] };
        var updated = await service.UpdateDraftAuthorizedAsync(definition.Id, draft.Id, edit, 0, subject, actor);
        if (updated.Id != draft.Id || updated.RevisionNumber != draft.RevisionNumber || updated.DraftVersion != 1 ||
            updated.CreatedAtUtc != draft.CreatedAtUtc) throw new InvalidOperationException("Draft identity changed on save.");
        await Refused(() => service.UpdateDraftAuthorizedAsync(definition.Id, draft.Id, command, 0, subject, actor), FailureKind.Conflict);
        await Refused(() => service.ValidateRevisionAuthorizedAsync(definition.Id, draft.Id, subject, actor, 0), FailureKind.Conflict);
        await Refused(() => service.UpdateDraftAuthorizedAsync(definition.Id, draft.Id,
            edit with { AssetBindings = [new("target", outsideAsset, "{}")] }, 1, subject, actor), FailureKind.Authorization);
        // A validator which read version 0 must not lock a subsequently edited version.
        await Refused(() => new ConfiguredLogicRepository(setup).SetRevisionValidatedAuthorizedAsync(draft, subject, actor), FailureKind.Conflict);
        var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailSave()).Options;
        await using (var failing = new WellSiteAutoPilotDbContext(failingOptions))
        {
            try { await Service(failing).UpdateDraftAuthorizedAsync(definition.Id, draft.Id, command, 1, subject, actor); }
            catch (InvalidOperationException e) when (e.Message == "edit-rollback-probe") { }
        }
        var persisted = (await new ConfiguredLogicRepository(setup).GetAsync(definition.Id))!;
        var saved = persisted.Revisions.Single(item => item.Id == draft.Id);
        if (persisted.ActiveRevisionId != active.Id || saved.DraftVersion != 1 || saved.Schedule?.CadenceSeconds != 300 ||
            saved.AssetBindings.Single().ParameterOverridesJson != updated.AssetBindings.Single().ParameterOverridesJson ||
            saved.ParametersJson != updated.ParametersJson || persisted.Revisions.Count != 2)
            throw new InvalidOperationException("Draft save/rollback altered active state, history or settings.");
        // Removing an unauthorized old target cannot turn a foreign draft into an owned one.
        var foreignRevision = draft with { Id = Guid.NewGuid(), ConfiguredLogicId = Guid.NewGuid(), RevisionNumber = 1,
            AssetBindings = [new("target", outsideAsset, "{}")] };
        await new ConfiguredLogicRepository(setup).AddAsync(new(foreignRevision.ConfiguredLogicId, "Foreign draft",
            null, foreignRevision.CreatedAtUtc, [foreignRevision]), actor);
        await Refused(() => new ConfiguredLogicRepository(setup).UpdateDraftAuthorizedAsync(foreignRevision with
        {
            DraftVersion = 1, AssetBindings = [new("target", asset, "{}")]
        }, 0, subject, actor), FailureKind.Authorization);
        await setup.ConfiguredLogicDefinitions.Where(item => item.Id == foreignRevision.ConfiguredLogicId).ExecuteDeleteAsync();
        // Race two editors on the same version; at most one may commit.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            try
            {
                await Service(db).UpdateDraftAuthorizedAsync(definition.Id, draft.Id,
                    edit with { ParametersJson = "{\"writer\":" + index + "}" }, 1, subject, actor);
                return true;
            }
            catch (WellSiteAutoPilotException e) when (e.Kind == FailureKind.Conflict) { return false; }
        }));
        if (outcomes.Count(value => value) != 1) throw new InvalidOperationException("Concurrent edits did not have one winner.");
        // Failure before validation commits keeps the draft editable.
        await setup.LogicModules.Where(item => item.ModuleId == draft.ModuleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, false));
        await Refused(() => service.ValidateRevisionAuthorizedAsync(definition.Id, draft.Id, subject, actor, 2), FailureKind.DependencyUnavailable);
        await setup.LogicModules.Where(item => item.ModuleId == draft.ModuleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, true));
        await service.UpdateDraftAuthorizedAsync(definition.Id, draft.Id, edit, 2, subject, actor);
        await service.ValidateRevisionAuthorizedAsync(definition.Id, draft.Id, subject, actor, 3);
        await Refused(() => service.UpdateDraftAuthorizedAsync(definition.Id, draft.Id, edit, 3, subject, actor), FailureKind.Conflict);
        await Refused(() => service.UpdateDraftAuthorizedAsync(definition.Id, active.Id, edit, 0, subject, actor), FailureKind.Conflict);
        if (await setup.AuditEvents.CountAsync(item => item.TargetId == definition.Id.ToString() && item.Action == "CONFIGURED_LOGIC_DRAFT_UPDATED") != 3)
            throw new InvalidOperationException("Denied or rolled-back draft edits added audit events.");
        await setup.ConfiguredLogicScheduleChanges.Where(item => item.ConfiguredLogicId == definition.Id).ExecuteDeleteAsync();
        await setup.ConfiguredLogicScheduleStates.Where(item => item.ConfiguredLogicId == definition.Id).ExecuteDeleteAsync();
        await setup.ConfiguredLogicDefinitions.Where(item => item.Id == definition.Id).ExecuteDeleteAsync();
        Console.WriteLine("Editable drafts preserve identity and active state; stale editors/validators, scope loss and locked revisions are refused atomically.");

        ConfiguredLogicService Service(WellSiteAutoPilotDbContext db) => new(new ConfiguredLogicRepository(db),
            new AssetRepository(db), new LogicModuleCatalogRepository(db), TimeProvider.System);
    }

    private static async Task Refused(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException e) when (e.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected draft refusal: {kind}.");
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("edit-rollback-probe");
    }
}
