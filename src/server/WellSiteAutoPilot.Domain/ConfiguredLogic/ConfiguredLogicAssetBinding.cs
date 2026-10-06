namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicAssetBinding(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);
