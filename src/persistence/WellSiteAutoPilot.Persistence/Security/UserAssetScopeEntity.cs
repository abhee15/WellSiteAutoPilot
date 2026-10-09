namespace WellSiteAutoPilot.Persistence.Security;

public sealed class UserAssetScopeEntity
{
    public Guid UserId { get; init; }
    public Guid AssetId { get; init; }
}
