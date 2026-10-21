namespace WellSiteAutoPilot.Api.Contracts.Assets;

/// <summary>
/// What a View shows. <c>Connections</c> holds Connection ids or "standalone" (empty: every visible owner);
/// <c>Activity</c> is all, active or inactive; <c>Grouping</c> is up to three of connection, assetType, status.
/// </summary>
public sealed record AssetViewDefinitionDto(
    int Version,
    IReadOnlyList<string> Connections,
    IReadOnlyList<Guid> AssetTypeIds,
    string Activity,
    string? Search,
    IReadOnlyList<string> Grouping);

/// <summary>A person's own saved arrangement of the Assets they may already see. It never grants access.</summary>
public sealed record AssetViewResponse(
    Guid Id, string Name, AssetViewDefinitionDto Definition, int Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CreateAssetViewRequest(string Name, AssetViewDefinitionDto Definition);

public sealed record UpdateAssetViewRequest(int ExpectedRevision, string Name, AssetViewDefinitionDto Definition);

public sealed record AssetViewGroupResponse(string Key, string Label, int Count);

/// <summary>The next level of a View: groups (<c>Level</c> says what they are) or, at the last level, Assets.</summary>
public sealed record AssetViewNodesResponse(
    string? Level, IReadOnlyList<AssetViewGroupResponse> Groups, IReadOnlyList<AssetTreeNodeResponse> Assets);
