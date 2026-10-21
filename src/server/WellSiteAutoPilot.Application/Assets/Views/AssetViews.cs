using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Assets.Views;

public static class AssetViewFailureCodes
{
    public const string NotFound = "ASSET_VIEW_NOT_FOUND";
    public const string NameConflict = "ASSET_VIEW_NAME_CONFLICT";
    public const string StaleRevision = "ASSET_VIEW_REVISION_STALE";
    public const string LimitReached = "ASSET_VIEW_LIMIT_REACHED";
    public const string ShareForbidden = "ASSET_VIEW_SHARE_FORBIDDEN";
    public const string SharedNameConflict = "ASSET_VIEW_SHARED_NAME_CONFLICT";
    public const string SharedLimitReached = "ASSET_VIEW_SHARED_LIMIT_REACHED";
}

/// <summary>Who can see a View. Personal is the owner alone; Shared is everyone who may read Assets (each seeing only what they may already see).</summary>
public enum AssetViewVisibility
{
    Personal = 0,
    Shared = 1
}

/// <summary>
/// What a View shows and how it is arranged. It is a query over the Assets the caller may already see; it never
/// copies, moves or creates Assets or Groups. <see cref="Connections"/> holds Connection ids or "standalone"; empty
/// means every visible owner. <see cref="Grouping"/> holds ordered levels from <see cref="AssetViewDefinitionRules.GroupingLevels"/>.
/// </summary>
public sealed record AssetViewDefinition(
    int Version,
    IReadOnlyList<string> Connections,
    IReadOnlyList<Guid> AssetTypeIds,
    string Activity,
    string? Search,
    IReadOnlyList<string> Grouping);

public sealed record AssetViewRecord(
    Guid Id, Guid OwnerUserId, string OwnerName, AssetViewVisibility Visibility, string Name, AssetViewDefinition Definition,
    int Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>One group at the next level of a View: a stable key to pass back, a readable label and how many Assets it holds.</summary>
public sealed record AssetViewGroup(string Key, string Label, int Count);

/// <summary>The next level of a View: groups (with <see cref="Level"/> saying what they are) or, at the last level, Assets.</summary>
public sealed record AssetViewNodes(string? Level, IReadOnlyList<AssetViewGroup> Groups, IReadOnlyList<AssetTreeNode> Assets);

public static class AssetViewDefinitionRules
{
    public const int CurrentVersion = 1;
    public const string Standalone = "standalone";
    public const string ByConnection = "connection";
    public const string ByAssetType = "assetType";
    public const string ByStatus = "status";
    public const int MaximumNameLength = 48;
    public const int MaximumViewsPerPerson = 50;
    public const int MaximumSharedViews = 100;
    public const int MaximumSearchLength = 120;
    private const int MaximumGroupingLevels = 3;
    private const int MaximumListed = 100;

    public static readonly IReadOnlyList<string> GroupingLevels = [ByConnection, ByAssetType, ByStatus];
    private static readonly string[] Activities = ["all", "active", "inactive"];

    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaximumNameLength)
            throw Invalid("Give the view a name of 1 to 48 characters.");
        return trimmed;
    }

    /// <summary>Checks a definition and returns it in canonical form (trimmed text, no empty search), or refuses it.</summary>
    public static AssetViewDefinition Normalize(AssetViewDefinition? definition)
    {
        if (definition is null) throw Invalid("A view needs a definition.");
        if (definition.Version != CurrentVersion) throw Invalid("This view definition version is not supported.");
        var connections = (definition.Connections ?? []).Select(item => item?.Trim() ?? string.Empty).ToArray();
        if (connections.Length > MaximumListed || connections.Distinct(StringComparer.OrdinalIgnoreCase).Count() != connections.Length ||
            connections.Any(item => !string.Equals(item, Standalone, StringComparison.OrdinalIgnoreCase) && !Guid.TryParse(item, out _)))
            throw Invalid("Choose each Connection, or Standalone, at most once.");
        var types = (definition.AssetTypeIds ?? []).ToArray();
        if (types.Length > MaximumListed || types.Distinct().Count() != types.Length)
            throw Invalid("Choose each Asset type at most once.");
        if (!Activities.Contains(definition.Activity, StringComparer.Ordinal))
            throw Invalid("Activity must be all, active or inactive.");
        var search = string.IsNullOrWhiteSpace(definition.Search) ? null : definition.Search.Trim();
        if (search is { Length: > MaximumSearchLength }) throw Invalid("Search text is limited to 120 characters.");
        var grouping = (definition.Grouping ?? []).ToArray();
        if (grouping.Length > MaximumGroupingLevels || grouping.Distinct(StringComparer.Ordinal).Count() != grouping.Length ||
            grouping.Any(level => !GroupingLevels.Contains(level, StringComparer.Ordinal)))
            throw Invalid("Group by Connection, Asset type or status, each at most once, up to three levels.");
        return new AssetViewDefinition(CurrentVersion,
            connections.Select(item => string.Equals(item, Standalone, StringComparison.OrdinalIgnoreCase)
                ? Standalone : Guid.Parse(item).ToString()).ToArray(),
            types, definition.Activity, search, grouping);
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new(FailureCodes.ValidationFailed, FailureKind.Validation, message);
}

/// <summary>
/// Views are personal unless their owner shares them. Reads are by viewer (own Views plus Shared ones); writes are by
/// owner, with an administrator exception to remove or unshare someone else's Shared View.
/// </summary>
public interface IAssetViewRepository
{
    /// <summary>The viewer's own Views and everyone's Shared Views, own first then by name.</summary>
    Task<IReadOnlyList<AssetViewRecord>> ListAsync(Guid viewerUserId, CancellationToken cancellationToken = default);

    /// <summary>The viewer's own View, or a Shared one. Another person's Personal View is never returned.</summary>
    Task<AssetViewRecord?> FindAsync(Guid viewerUserId, Guid id, CancellationToken cancellationToken = default);

    Task<AssetViewRecord> CreateAsync(Guid ownerUserId, string name, AssetViewDefinition definition,
        AssetViewVisibility visibility, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Refused as stale when <paramref name="expectedRevision"/> is not the stored revision.</summary>
    Task<AssetViewRecord> UpdateAsync(Guid ownerUserId, Guid id, int expectedRevision, string name,
        AssetViewDefinition definition, AssetViewVisibility visibility, CancellationToken cancellationToken = default);

    /// <summary>The owner, or an administrator for a Shared View.</summary>
    Task DeleteAsync(Guid callerUserId, bool asAdministrator, Guid id, int expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>An administrator makes someone's Shared View Personal again. Does nothing to a Personal View.</summary>
    Task<AssetViewRecord> UnshareAsync(Guid id, CancellationToken cancellationToken = default);
}
