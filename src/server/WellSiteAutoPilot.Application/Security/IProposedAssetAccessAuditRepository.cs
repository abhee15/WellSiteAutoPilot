namespace WellSiteAutoPilot.Application.Security;

public interface IProposedAssetAccessAuditRepository
{
    Task RecordAsync(ProposedAssetAccessView view, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
