namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicDataBinding(
    string RequirementId,
    Guid AssetId,
    string ProviderId,
    string ProviderAssetExternalId,
    string ProviderMappingJson);
