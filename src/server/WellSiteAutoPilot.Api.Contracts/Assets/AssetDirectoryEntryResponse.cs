namespace WellSiteAutoPilot.Api.Contracts.Assets;

/// <summary>
/// Minimal administrative Asset directory entry for scoped-grant planning.
/// Avoid transferring large per-Asset attribute documents while searching.
/// </summary>
public sealed record AssetDirectoryEntryResponse(
    Guid Id,
    string Name,
    bool IsActive);
