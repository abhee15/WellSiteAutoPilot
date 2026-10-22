namespace WellSiteAutoPilot.Api.Contracts.Assets;

/// <summary>Visible to every authorized reader of its Connection; membership is explicit and never grants access.</summary>
public sealed record AssetViewResponse(Guid Id, Guid ConnectionId, string Name, int Revision, int MemberCount,
    bool HasUnavailableMembers, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, bool CanEdit, bool CanDelete);

public sealed record CreateAssetViewRequest(string Name, Guid ConnectionId, IReadOnlyList<Guid> AssetIds);
public sealed record UpdateAssetViewRequest(int ExpectedRevision, string Name, IReadOnlyList<Guid> AssetIds);
public sealed record AssetViewSelectionResponse(int Revision, IReadOnlyList<Guid> AssetIds);
