namespace WellSiteAutoPilot.Application.Executions;

public sealed record ShadowExecutionCommand(
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid AssetId,
    string AssetExternalId,
    string Quantity);
