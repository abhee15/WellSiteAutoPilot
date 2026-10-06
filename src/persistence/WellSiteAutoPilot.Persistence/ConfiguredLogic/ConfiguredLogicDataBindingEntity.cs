namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicDataBindingEntity
{
    public Guid RevisionId { get; init; }
    public string RequirementId { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderAssetExternalId { get; set; } = string.Empty;
    public string ProviderMappingJson { get; set; } = "{}";
}
