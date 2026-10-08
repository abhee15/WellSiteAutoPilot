using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Persistence.Recommendations;

public sealed class RecommendationRepository(
    WellSiteAutoPilotDbContext dbContext) : IRecommendationRepository
{
    public async Task<int> AddMissingAsync(
        IReadOnlyCollection<RecommendationRecord> recommendations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendations);

        if (recommendations.Count == 0)
        {
            return 0;
        }

        var executionIds = recommendations
            .Select(item => item.ExecutionId)
            .Distinct()
            .ToArray();

        var existing = await dbContext.Recommendations
            .AsNoTracking()
            .Where(item => executionIds.Contains(item.ExecutionId))
            .Select(item => new { item.ExecutionId, item.IntentIndex })
            .ToArrayAsync(cancellationToken);

        var existingKeys = existing
            .Select(item => (item.ExecutionId, item.IntentIndex))
            .ToHashSet();

        var added = 0;

        foreach (var recommendation in recommendations)
        {
            if (existingKeys.Contains(
                    (recommendation.ExecutionId, recommendation.IntentIndex)))
            {
                continue;
            }

            dbContext.Recommendations.Add(ToEntity(recommendation));
            existingKeys.Add(
                (recommendation.ExecutionId, recommendation.IntentIndex));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    public async Task<IReadOnlyCollection<RecommendationRecord>> ListByExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Recommendations
            .AsNoTracking()
            .Where(item => item.ExecutionId == executionId)
            .OrderBy(item => item.IntentIndex)
            .ToArrayAsync(cancellationToken))
        .Select(ToDomain)
        .ToArray();

    private static RecommendationEntity ToEntity(
        RecommendationRecord recommendation) => new()
        {
            Id = recommendation.Id,
            ExecutionId = recommendation.ExecutionId,
            IntentIndex = recommendation.IntentIndex,
            ConfiguredLogicId = recommendation.ConfiguredLogicId,
            ConfigurationRevisionId = recommendation.ConfigurationRevisionId,
            ModuleId = recommendation.ModuleId,
            ModuleVersion = recommendation.ModuleVersion,
            AssetId = recommendation.AssetId,
            Code = recommendation.Code,
            Command = recommendation.Command,
            Quantity = recommendation.Quantity,
            SuggestedValue = recommendation.SuggestedValue,
            Unit = recommendation.Unit,
            ReasonCode = recommendation.ReasonCode,
            IntentJson = recommendation.IntentJson,
            Status = recommendation.Status.ToString(),
            CreatedAtUtc = recommendation.CreatedAtUtc,
            ExpiresAtUtc = recommendation.ExpiresAtUtc,
            DecisionBy = recommendation.DecisionBy,
            DecisionAtUtc = recommendation.DecisionAtUtc,
            DecisionReason = recommendation.DecisionReason,
            ControlActionId = recommendation.ControlActionId
        };

    private static RecommendationRecord ToDomain(
        RecommendationEntity entity) => new(
            entity.Id,
            entity.ExecutionId,
            entity.IntentIndex,
            entity.ConfiguredLogicId,
            entity.ConfigurationRevisionId,
            entity.ModuleId,
            entity.ModuleVersion,
            entity.AssetId,
            entity.Code,
            entity.Command,
            entity.Quantity,
            entity.SuggestedValue,
            entity.Unit,
            entity.ReasonCode,
            entity.IntentJson,
            Enum.Parse<RecommendationStatus>(entity.Status),
            entity.CreatedAtUtc,
            entity.ExpiresAtUtc,
            entity.DecisionBy,
            entity.DecisionAtUtc,
            entity.DecisionReason,
            entity.ControlActionId);
}
