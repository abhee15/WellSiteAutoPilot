using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>One authorized asset with how many authorized assets sit directly below it.</summary>
public sealed record AssetTreeNode(Asset Asset, int ChildCount);

/// <summary>One step of the location of an asset, nearest the top first.</summary>
public sealed record AssetPathSegment(Guid Id, string Name);

/// <summary>A search match with the authorized ancestors it sits under.</summary>
public sealed record AssetSearchHit(Asset Asset, IReadOnlyList<AssetPathSegment> Path);
