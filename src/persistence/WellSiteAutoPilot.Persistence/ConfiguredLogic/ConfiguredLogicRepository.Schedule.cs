using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    public async Task<ConfiguredLogicScheduleState> ApplyScheduleChangeAuthorizedAsync(
        ConfiguredLogicScheduleChangeRequest request, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        ConfiguredLogicScheduleStateEntity? staged = null;
        await WriteConfigurationAuthorizedAsync(subject, actor, async () =>
        {
            var definition = await LockConfigurationAsync(request.ConfiguredLogicId, cancellationToken);
            // Same scope as activation: every Asset any revision touches, so an older revision cannot hide one.
            var targets = definition.Revisions.SelectMany(DraftAssets).Distinct().Order().ToArray();
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            staged = await ConfiguredLogicScheduleWriter.StageAsync(
                dbContext, definition.ActiveRevisionId, request with { ChangedBy = actor.IdentityName }, actor, cancellationToken);
        }, cancellationToken);
        return ConfiguredLogicScheduleRepository.ToState(staged ?? throw new InvalidOperationException());
    }
}
