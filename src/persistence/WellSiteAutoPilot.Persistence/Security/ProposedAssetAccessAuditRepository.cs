using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class ProposedAssetAccessAuditRepository(WellSiteAutoPilotDbContext db)
    : IProposedAssetAccessAuditRepository
{
    public Task RecordAsync(ProposedAssetAccessView view, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        return SecurityDiagnosticAuditWriter.RecordAsync(db, actor, nowUtc,
            "security.access.assets-previewed", "security-user", view.UserId.ToString("D"), null,
            new { view.Permission, view.Offset, view.Limit, returnedCount = view.Assets.Count,
                diagnosticOnly = true, liveAuthorizationUnchanged = true }, cancellationToken);
    }
}
