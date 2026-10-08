namespace WellSiteAutoPilot.Persistence.Executions;

public sealed class ExecutionAssetScopeEntity
{
    public Guid ExecutionId { get; init; }
    public Guid LogicInstanceId { get; init; }
    public Guid AssetId { get; init; }
}
