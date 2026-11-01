using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;

namespace WellSiteAutoPilot.Api.Contracts.Assets;

public sealed record AssetLogicSummaryResponse(ConfiguredLogicResponse Configuration, DateTimeOffset? NextScheduledAtUtc);
public sealed record AssetLogicPageResponse(bool IsGroupingNode, AssetLogicSummaryResponse[] Items, bool HasMore);
