using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Assets.Views;

public static class AssetViewFailureCodes
{
    public const string NotFound = "ASSET_VIEW_NOT_FOUND";
    public const string NameConflict = "ASSET_VIEW_NAME_CONFLICT";
    public const string StaleRevision = "ASSET_VIEW_REVISION_STALE";
}

/// <summary>A named, explicit set of wells from exactly one Connection. Creator identity is audit data only.</summary>
public sealed record AssetViewRecord(Guid Id, Guid ConnectionId, string Name, int Revision,
    int MemberCount, bool HasUnavailableMembers, bool CanInspectAllMembers, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record AssetViewSelection(int Revision, IReadOnlyList<Guid> AssetIds, IReadOnlyList<AssetViewUnavailableMember> UnavailableMembers);
public sealed record AssetViewUnavailableMember(Guid Id, string Name);

public static class AssetViewDefinitionRules
{
    public const int MaximumNameLength = 48;
    public const int MaximumMembers = 50_000;

    public static string NormalizeName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > MaximumNameLength) throw Invalid("Give the View a name of 1 to 48 characters.");
        return value;
    }

    public static Guid[] NormalizeMembers(IReadOnlyList<Guid>? assetIds)
    {
        if (assetIds is null || assetIds.Count is < 1 or > MaximumMembers || assetIds.Contains(Guid.Empty) ||
            assetIds.Distinct().Count() != assetIds.Count)
            throw Invalid("Select between 1 and 50,000 different wells from the Connection.");
        return assetIds.Order().ToArray();
    }

    public static WellSiteAutoPilotException Invalid(string message) =>
        new(FailureCodes.ValidationFailed, FailureKind.Validation, message);
}

/// <summary>Connection access controls visibility. All mutation authority is checked again inside the transaction.</summary>
public interface IAssetViewRepository
{
    Task<IReadOnlyList<AssetViewRecord>> ListAsync(AccessSubject subject, CancellationToken cancellationToken = default);
    Task<AssetViewRecord?> FindAsync(AccessSubject subject, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Asset>> MembersAsync(AccessSubject subject, Guid id, string? search, int offset, int limit,
        CancellationToken cancellationToken = default);
    Task<AssetViewSelection> SelectionAsync(AccessSubject subject, Guid id, CancellationToken cancellationToken = default);
    Task<AssetViewRecord> CreateAsync(AccessSubject subject, SecurityActorContext actor, Guid connectionId,
        string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<AssetViewRecord> UpdateAsync(AccessSubject subject, SecurityActorContext actor, Guid id, int expectedRevision,
        string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);
    Task DeleteAsync(AccessSubject subject, SecurityActorContext actor, Guid id, int expectedRevision,
        CancellationToken cancellationToken = default);
}
