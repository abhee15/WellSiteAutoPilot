using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;
using WellSiteAutoPilot.Api.Contracts.Executions;

namespace WellSiteAutoPilot.Api.Contracts.Assets;

public sealed record AssetLogicSummaryResponse(ConfiguredLogicResponse Configuration, DateTimeOffset? NextScheduledAtUtc);
public sealed record AssetLogicPageResponse(bool IsGroupingNode, AssetLogicSummaryResponse[] Items, bool HasMore);
public sealed record AssetRunHistoryResponse(ExecutionResponse[] Items, Guid? NextCursor);
