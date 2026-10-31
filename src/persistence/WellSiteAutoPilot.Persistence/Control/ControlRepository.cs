using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Control;

public sealed class ControlRepository(WellSiteAutoPilotDbContext dbContext) : IControlRepository
{
    private const string GateActor = "system:autonomy-gate";
    private const int RateLockClass = 7101;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AutonomyPolicy?> GetPolicyAsync(Guid configuredLogicId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.AutonomyPolicies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ConfiguredLogicId == configuredLogicId, cancellationToken);
        return entity is null ? null : new AutonomyPolicy(
            entity.ConfiguredLogicId, entity.Enabled, entity.SinkKind,
            JsonSerializer.Deserialize<AutonomyCommandLimit[]>(entity.LimitsJson, Json) ?? [],
            entity.MaxActionsPerWindow, entity.WindowSeconds, entity.UpdatedBy, entity.UpdatedAtUtc,
            entity.AllowApprovedRecommendations);
    }

    public async Task UpsertPolicyAsync(AutonomyPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var entity = await dbContext.AutonomyPolicies
            .SingleOrDefaultAsync(item => item.ConfiguredLogicId == policy.ConfiguredLogicId, cancellationToken);
        if (entity is null)
        {
            entity = new AutonomyPolicyEntity { ConfiguredLogicId = policy.ConfiguredLogicId };
            dbContext.AutonomyPolicies.Add(entity);
        }

        entity.Enabled = policy.Enabled;
        entity.SinkKind = policy.SinkKind;
        entity.LimitsJson = JsonSerializer.Serialize(policy.Limits, Json);
        entity.MaxActionsPerWindow = policy.MaxActionsPerWindow;
        entity.WindowSeconds = policy.WindowSeconds;
        entity.AllowApprovedRecommendations = policy.AllowApprovedRecommendations;
        entity.UpdatedBy = policy.UpdatedBy;
        entity.UpdatedAtUtc = policy.UpdatedAtUtc;
        dbContext.AuditEvents.Add(Audit(policy.UpdatedAtUtc, policy.UpdatedBy, "control.policy.updated",
            "configured-logic", policy.ConfiguredLogicId.ToString(), null,
            new { policy.Enabled, policy.SinkKind, policy.Limits, policy.MaxActionsPerWindow, policy.WindowSeconds, policy.AllowApprovedRecommendations }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ControlKillSwitch> GetKillSwitchAsync(CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ControlKillSwitches.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        // Fail closed if the singleton row is ever missing.
        return entity is null
            ? new ControlKillSwitch(true, null, null, "KILL_SWITCH_STATE_MISSING")
            : new ControlKillSwitch(entity.Engaged, entity.ChangedBy, entity.ChangedAtUtc, entity.Reason);
    }

    public async Task SetKillSwitchAsync(bool engaged, string actor, string? reason, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ControlKillSwitches.Where(item => item.Id == 1).ExecuteUpdateAsync(set => set
            .SetProperty(item => item.Engaged, engaged)
            .SetProperty(item => item.ChangedBy, actor)
            .SetProperty(item => item.ChangedAtUtc, nowUtc)
            .SetProperty(item => item.Reason, reason), cancellationToken);
        dbContext.AuditEvents.Add(Audit(nowUtc, actor,
            engaged ? "control.kill-switch.engaged" : "control.kill-switch.released",
            "control", "kill-switch", null, new { Reason = reason }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<(ControlClaimOutcome Outcome, ControlActionRecord? Action)> TryClaimAsync(
        ControlClaim claim, CancellationToken cancellationToken = default)
    {
        var rec = claim.Recommendation;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var key = BinaryPrimitives.ReadInt32BigEndian(rec.ConfiguredLogicId.ToByteArray());
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({RateLockClass}, {key})", cancellationToken);

        var since = claim.NowUtc.AddSeconds(-claim.WindowSeconds);
        var recent = await dbContext.ControlActions.CountAsync(item =>
            item.ConfiguredLogicId == rec.ConfiguredLogicId && item.CreatedAtUtc >= since &&
            (item.Status == nameof(ControlActionStatus.Requested) ||
             item.Status == nameof(ControlActionStatus.Dispatched) ||
             item.Status == nameof(ControlActionStatus.Verified) ||
             item.Status == nameof(ControlActionStatus.Ambiguous)), cancellationToken);
        if (recent >= claim.MaxActionsPerWindow)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (ControlClaimOutcome.RateLimited, null);
        }

        if (claim.Origin == ControlOrigins.ApprovedRecommendation)
        {
            // Operator approval is the authority; idempotency is the unique action per recommendation.
            var eligible = await dbContext.Recommendations.AnyAsync(item => item.Id == rec.Id &&
                item.Status == nameof(RecommendationStatus.Approved) &&
                (item.ExpiresAtUtc == null || item.ExpiresAtUtc > claim.NowUtc) &&
                dbContext.Executions.Any(execution => execution.Id == item.ExecutionId &&
                    execution.Mode == nameof(ExecutionMode.Recommendation) &&
                    execution.Status == nameof(ExecutionStatus.Completed)), cancellationToken);
            if (!eligible || await dbContext.ControlActions.AnyAsync(item => item.RecommendationId == rec.Id, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (ControlClaimOutcome.AlreadyDecided, null);
            }
        }
        else if (await dbContext.Recommendations
            .Where(item => item.Id == rec.Id && item.Status == nameof(RecommendationStatus.Created) &&
                dbContext.Executions.Any(execution => execution.Id == item.ExecutionId &&
                    execution.Mode == nameof(ExecutionMode.Autonomous) &&
                    execution.Status == nameof(ExecutionStatus.Completed)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, nameof(RecommendationStatus.Approved))
                .SetProperty(item => item.DecisionBy, GateActor)
                .SetProperty(item => item.DecisionAtUtc, claim.NowUtc)
                .SetProperty(item => item.DecisionReason, GateCodes.Allowed), cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (ControlClaimOutcome.AlreadyDecided, null);
        }

        var action = NewAction(rec, ControlActionStatus.Requested, GateCodes.Allowed, claim.SinkKind, claim.NowUtc) with
        { Origin = claim.Origin };
        dbContext.ControlActions.Add(ToEntity(action));
        dbContext.AuditEvents.Add(Audit(claim.NowUtc, GateActor, "control.action.requested", "control-action",
            action.Id.ToString(), rec.AssetId, new { RecommendationId = rec.Id, rec.Command, Value = rec.SuggestedValue, rec.Unit, claim.SinkKind }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Recommendations.Where(item => item.Id == rec.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ControlActionId, action.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (ControlClaimOutcome.Claimed, action);
    }

    public async Task CompleteAsync(Guid actionId, ControlActionStatus status, string gateCode, string? sinkReference,
        DateTimeOffset nowUtc, decimal? readbackValue = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var action = await dbContext.ControlActions.AsNoTracking()
            .SingleAsync(item => item.Id == actionId, cancellationToken);
        var updated = await dbContext.ControlActions.Where(item => item.Id == actionId &&
                item.Status == nameof(ControlActionStatus.Requested))
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, status.ToString())
                .SetProperty(item => item.GateCode, gateCode)
                .SetProperty(item => item.SinkReference, sinkReference)
                .SetProperty(item => item.ReadbackValue, readbackValue)
                .SetProperty(item => item.VerifiedAtUtc, status == ControlActionStatus.Verified ? nowUtc : (DateTimeOffset?)null)
                .SetProperty(item => item.CompletedAtUtc, nowUtc), cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        if (status != ControlActionStatus.Ambiguous)
        {
            var recommendationStatus = status is ControlActionStatus.Dispatched or ControlActionStatus.Verified
                ? RecommendationStatus.Executed : RecommendationStatus.Invalidated;
            await dbContext.Recommendations.Where(item => item.Id == action.RecommendationId &&
                    item.Status == nameof(RecommendationStatus.Approved))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.Status, recommendationStatus.ToString())
                    .SetProperty(item => item.DecisionReason, gateCode), cancellationToken);
        }

        dbContext.AuditEvents.Add(Audit(nowUtc, GateActor, $"control.action.{status.ToString().ToLowerInvariant()}",
            "control-action", actionId.ToString(), action.AssetId, new { GateCode = gateCode, SinkReference = sinkReference, action.Sink, ReadbackValue = readbackValue }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ControlActionRecord?> TransitionAsync(Guid actionId, IReadOnlyCollection<ControlActionStatus> from,
        ControlActionStatus target, string gateCode, decimal? readbackValue, string actor, string? note, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var fromNames = from.Select(item => item.ToString()).ToArray();
        var action = await dbContext.ControlActions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == actionId, cancellationToken);
        var mergedReadback = readbackValue ?? action?.ReadbackValue;
        DateTimeOffset? mergedVerifiedAt = target == ControlActionStatus.Verified ? nowUtc : action?.VerifiedAtUtc;
        var updated = action is null ? 0 : await dbContext.ControlActions.Where(item => item.Id == actionId &&
                fromNames.Contains(item.Status))
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, target.ToString())
                .SetProperty(item => item.GateCode, gateCode)
                .SetProperty(item => item.ReadbackValue, mergedReadback)
                .SetProperty(item => item.VerifiedAtUtc, mergedVerifiedAt)
                .SetProperty(item => item.ResolutionNote, note), cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (target == ControlActionStatus.Ambiguous)
        {
            // The command may not have landed: the recommendation is no longer considered executed.
            await dbContext.Recommendations.Where(item => item.Id == action!.RecommendationId &&
                    item.Status == nameof(RecommendationStatus.Executed))
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, nameof(RecommendationStatus.Approved)),
                    cancellationToken);
        }

        if (target is ControlActionStatus.Verified or ControlActionStatus.Failed)
        {
            var recommendationStatus = target == ControlActionStatus.Verified
                ? RecommendationStatus.Executed : RecommendationStatus.Invalidated;
            await dbContext.Recommendations.Where(item => item.Id == action!.RecommendationId &&
                    (item.Status == nameof(RecommendationStatus.Approved) ||
                     item.Status == nameof(RecommendationStatus.Executed)))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.Status, recommendationStatus.ToString())
                    .SetProperty(item => item.DecisionReason, gateCode), cancellationToken);
        }

        dbContext.AuditEvents.Add(Audit(nowUtc, actor, $"control.action.{target.ToString().ToLowerInvariant()}",
            "control-action", actionId.ToString(), action!.AssetId,
            new { From = fromNames, GateCode = gateCode, ReadbackValue = readbackValue, Note = note }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetActionAsync(actionId, cancellationToken);
    }

    public async Task<ControlActionRecord?> GetActionAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ControlActions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == actionId, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<ControlActionRecord?> GetActionByRecommendationAsync(Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ControlActions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RecommendationId == recommendationId, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task RecordRefusalAsync(RecommendationRecord recommendation, string gateCode, string actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(Audit(nowUtc, actor, "control.action.refused", "recommendation",
            recommendation.Id.ToString(), recommendation.AssetId,
            new { GateCode = gateCode, recommendation.Command, Value = recommendation.SuggestedValue }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordBlockedAsync(RecommendationRecord recommendation, string gateCode, string sinkKind,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rejected = await dbContext.Recommendations
            .Where(item => item.Id == recommendation.Id && item.Status == nameof(RecommendationStatus.Created))
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, nameof(RecommendationStatus.Rejected))
                .SetProperty(item => item.DecisionBy, GateActor)
                .SetProperty(item => item.DecisionAtUtc, nowUtc)
                .SetProperty(item => item.DecisionReason, gateCode), cancellationToken);
        if (rejected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var action = NewAction(recommendation, ControlActionStatus.Blocked, gateCode, sinkKind, nowUtc) with
        { CompletedAtUtc = nowUtc };
        dbContext.ControlActions.Add(ToEntity(action));
        dbContext.AuditEvents.Add(Audit(nowUtc, GateActor, "control.action.blocked", "control-action",
            action.Id.ToString(), recommendation.AssetId, new { RecommendationId = recommendation.Id, GateCode = gateCode }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Recommendations.Where(item => item.Id == recommendation.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ControlActionId, action.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(Guid? configuredLogicId, int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ControlActions.AsNoTracking().AsQueryable();
        if (configuredLogicId is { } id) query = query.Where(item => item.ConfiguredLogicId == id);
        return (await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
                .Take(limit).ToArrayAsync(cancellationToken)).Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyCollection<Guid>> ListExecutionsWithUndecidedAutonomousRecommendationsAsync(int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.Recommendations.AsNoTracking()
            .Where(item => item.Status == nameof(RecommendationStatus.Created) &&
                dbContext.Executions.Any(execution => execution.Id == item.ExecutionId &&
                    execution.Mode == nameof(ExecutionMode.Autonomous)))
            .Select(item => item.ExecutionId).Distinct().Take(limit).ToArrayAsync(cancellationToken);

    private static ControlActionRecord NewAction(RecommendationRecord rec, ControlActionStatus status, string gateCode,
        string sink, DateTimeOffset now) =>
        new(Guid.NewGuid(), rec.Id, rec.ExecutionId, rec.ConfiguredLogicId, rec.AssetId, rec.Command, rec.Quantity,
            rec.SuggestedValue, rec.Unit, status, gateCode, sink, null, now);

    private static ControlActionEntity ToEntity(ControlActionRecord item) => new()
    {
        Id = item.Id, RecommendationId = item.RecommendationId, ExecutionId = item.ExecutionId,
        ConfiguredLogicId = item.ConfiguredLogicId, AssetId = item.AssetId, Command = item.Command,
        Quantity = item.Quantity, Value = item.Value, Unit = item.Unit, Status = item.Status.ToString(),
        GateCode = item.GateCode, Sink = item.Sink, SinkReference = item.SinkReference,
        CreatedAtUtc = item.CreatedAtUtc, CompletedAtUtc = item.CompletedAtUtc, Origin = item.Origin,
        ReadbackValue = item.ReadbackValue, VerifiedAtUtc = item.VerifiedAtUtc, ResolutionNote = item.ResolutionNote
    };

    private static ControlActionRecord ToDomain(ControlActionEntity item) => new(
        item.Id, item.RecommendationId, item.ExecutionId, item.ConfiguredLogicId, item.AssetId, item.Command,
        item.Quantity, item.Value, item.Unit, Enum.Parse<ControlActionStatus>(item.Status), item.GateCode,
        item.Sink, item.SinkReference, item.CreatedAtUtc, item.CompletedAtUtc, item.Origin, item.ReadbackValue,
        item.VerifiedAtUtc, item.ResolutionNote);

    private static AuditEventEntity Audit(DateTimeOffset at, string actor, string action, string targetType,
        string targetId, Guid? assetId, object details) => new()
    {
        Id = Guid.NewGuid(), OccurredAtUtc = at.ToUniversalTime(), ActorIdentity = actor, Action = action,
        TargetType = targetType, TargetId = targetId, AssetId = assetId,
        DetailsJson = JsonSerializer.Serialize(details, Json)
    };
}
