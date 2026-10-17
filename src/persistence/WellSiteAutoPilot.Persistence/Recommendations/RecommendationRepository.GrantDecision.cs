using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Recommendations;

public sealed partial class RecommendationRepository
{
    public async Task<RecommendationRecord> DecideAuthorizedAsync(AccessSubject subject, Guid recommendationId,
        RecommendationStatus decision, SecurityActorContext actor, string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (recommendationId == Guid.Empty || actor.UserId != subject.UserId ||
            string.IsNullOrWhiteSpace(actor.IdentityName) || actor.IdentityName.Length > 256 ||
            actor.IdentityName.Any(char.IsControl) ||
            decision is not (RecommendationStatus.Approved or RecommendationStatus.Rejected) ||
            (reason is not null && (reason.Length > 1024 || reason.Any(char.IsControl))))
            throw new WellSiteAutoPilotException("RECOMMENDATION_DECISION_INVALID", FailureKind.Validation,
                "A bound authenticated actor, recommendation and supported decision metadata are required.");
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // The target Asset comes from the locked persisted recommendation,
            // never a request body or a pre-transaction application snapshot.
            var rows = await dbContext.Recommendations.FromSqlInterpolated($"""
                SELECT r.* FROM operations.recommendations AS r WHERE r."Id" = {recommendationId} FOR UPDATE OF r
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (rows.Length != 1) throw Denied();
            var target = rows[0];
            var time = clock ?? TimeProvider.System;
            await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.RecommendationsDecide,
                [target.AssetId], time, cancellationToken);

            if (decision == RecommendationStatus.Approved)
            {
                // Approval depends on these mutable persisted states. Retain
                // them through commit as well as retaining the grant/Asset.
                var definitions = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated($"""
                    SELECT c.* FROM logic.configured_logic AS c WHERE c."Id" = {target.ConfiguredLogicId} FOR SHARE OF c
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
                var executions = await dbContext.Executions.FromSqlInterpolated($"""
                    SELECT e.* FROM operations.executions AS e WHERE e."Id" = {target.ExecutionId} FOR SHARE OF e
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
                var scopes = await dbContext.ExecutionAssetScopes.FromSqlInterpolated($"""
                    SELECT s.* FROM operations.execution_asset_scopes AS s
                    WHERE s."ExecutionId" = {target.ExecutionId} AND s."AssetId" = {target.AssetId} FOR SHARE OF s
                    """).AsNoTracking().ToArrayAsync(cancellationToken);
                if (definitions.Length != 1 || definitions[0].ActiveRevisionId != target.ConfigurationRevisionId ||
                    executions.Length != 1 || executions[0].Mode != nameof(ExecutionMode.Recommendation) ||
                    executions[0].Status != nameof(ExecutionStatus.Completed) ||
                    executions[0].LogicInstanceId != target.ConfiguredLogicId ||
                    executions[0].ConfigurationRevisionId != target.ConfigurationRevisionId ||
                    executions[0].RequestContractVersion is not (1 or 2) ||
                    (executions[0].RequestContractVersion == 1 && executions[0].AssetId != target.AssetId) ||
                    scopes.Length != 1 || scopes[0].LogicInstanceId != target.ConfiguredLogicId) throw Conflict();
            }
            // Check again after business-state locks, which may have waited
            // long enough for a group observation to expire.
            await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.RecommendationsDecide,
                [target.AssetId], time, cancellationToken);
            var now = time.GetUtcNow();
            var affected = await dbContext.Recommendations.Where(item => item.Id == recommendationId &&
                    (item.Status == nameof(RecommendationStatus.Created) || item.Status == nameof(RecommendationStatus.Pending)) &&
                    (item.ExpiresAtUtc == null || item.ExpiresAtUtc > now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, decision.ToString())
                    .SetProperty(item => item.DecisionBy, actor.IdentityName.Trim())
                    .SetProperty(item => item.DecisionAtUtc, now).SetProperty(item => item.DecisionReason, reason), cancellationToken);
            if (affected != 1) throw Conflict();
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = now, ActorUserId = subject.UserId, ActorIdentity = actor.IdentityName.Trim(),
                Action = decision == RecommendationStatus.Approved ? "recommendation.approved" : "recommendation.rejected",
                TargetType = "recommendation", TargetId = recommendationId.ToString(), AssetId = target.AssetId,
                CorrelationId = actor.CorrelationId,
                DetailsJson = JsonSerializer.Serialize(new { Decision = decision.ToString(), Reason = reason })
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            var result = await dbContext.Recommendations.AsNoTracking().SingleAsync(item => item.Id == recommendationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDomain(result);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException("SECURITY_ACCESS_CONFLICT", FailureKind.Conflict,
                "Recommendation or access changed concurrently. Reload before retrying.", innerException: exception);
        }

        static WellSiteAutoPilotException Denied() => new("SECURITY_SCOPE_DENIED", FailureKind.Authorization,
            "The current user is not authorized to decide this recommendation.");
        static WellSiteAutoPilotException Conflict() => new("RECOMMENDATION_DECISION_CONFLICT", FailureKind.Conflict,
            "Recommendation is no longer eligible for this decision.");
    }
}
