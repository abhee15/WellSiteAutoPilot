namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicAssetBindingEntity
{
    public Guid RevisionId { get; init; }
    public string Role { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public string ParameterOverridesJson { get; set; } = "{}";
}
