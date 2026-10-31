using System.Text.Json;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Control;

public sealed class DelegatedAuthorityAuditWriter(WellSiteAutoPilotDbContext dbContext) : IDelegatedAuthorityAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(DateTimeOffset atUtc, string action, Guid principalId, Guid configuredLogicId,
        object details, CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = atUtc.ToUniversalTime(),
            ActorIdentity = $"service-principal:{principalId}",
            Action = action,
            TargetType = "configured-logic",
            TargetId = configuredLogicId.ToString(),
            DetailsJson = JsonSerializer.Serialize(details, Json)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
