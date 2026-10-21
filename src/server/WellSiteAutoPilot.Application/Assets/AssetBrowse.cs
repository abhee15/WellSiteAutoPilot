using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>One authorized asset with how many authorized assets sit directly below it.</summary>
public sealed record AssetTreeNode(Asset Asset, int ChildCount);

/// <summary>One step of the location of an asset, nearest the top first.</summary>
public sealed record AssetPathSegment(Guid Id, string Name);

/// <summary>
/// Narrows the top level of the hierarchy to what one Connection owns (<see cref="ConnectionId"/>) or, when it is
/// null, to the standalone Assets that no Connection owns.
/// </summary>
public sealed record AssetOwnerScope(Guid? ConnectionId);

/// <summary>
/// One top-level group of the hierarchy: a Connection (with its current name) or the standalone Assets. Counts only
/// include Assets the caller may already see, and a group with none is not listed, so nothing leaks through it.
/// </summary>
public sealed record AssetOwnerSummary(Guid? ConnectionId, string? ConnectionName, bool IsEnabled, int TopLevelCount, int AssetCount);

/// <summary>A search match with the authorized ancestors it sits under.</summary>
public sealed record AssetSearchHit(Asset Asset, IReadOnlyList<AssetPathSegment> Path);
