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

/// <summary>
/// A saved arrangement of the Assets a person may already see; it never grants access. <c>Visibility</c> is personal or
/// shared. For someone other than the owner the definition lists only the Connections they can see and
/// <c>HiddenConnectionCount</c> says how many it left out. <c>CanEdit</c> is true only for the owner (and, for a
/// shared View, only while they may still share).
/// </summary>
public sealed record AssetViewResponse(
    Guid Id, string Name, AssetViewDefinitionDto Definition, int Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    string Visibility, string OwnerName, bool IsOwner, bool CanEdit, int HiddenConnectionCount);

/// <summary><c>Visibility</c> is "personal" (the default) or "shared"; sharing needs the asset-views.share permission.</summary>
public sealed record CreateAssetViewRequest(string Name, AssetViewDefinitionDto Definition, string? Visibility = null);

/// <summary>An omitted <c>Visibility</c> keeps the current one.</summary>
public sealed record UpdateAssetViewRequest(int ExpectedRevision, string Name, AssetViewDefinitionDto Definition, string? Visibility = null);

/// <summary>Makes a personal copy of a View the caller can read.</summary>
public sealed record CopyAssetViewRequest(string Name);

public sealed record AssetViewGroupResponse(string Key, string Label, int Count);

/// <summary>The next level of a View: groups (<c>Level</c> says what they are) or, at the last level, Assets.</summary>
public sealed record AssetViewNodesResponse(
    string? Level, IReadOnlyList<AssetViewGroupResponse> Groups, IReadOnlyList<AssetTreeNodeResponse> Assets);
