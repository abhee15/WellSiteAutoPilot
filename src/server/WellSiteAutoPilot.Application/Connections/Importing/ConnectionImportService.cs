using System.Text.Json;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Importing;

/// <summary>What importing would do, for review. Nothing has been saved.</summary>
public sealed record ImportPreview(
    ImportPlan Plan,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> TypesToCreate,
    bool CanImport,
    string? ActingAs);

/// <summary>Plans an import of what a connection holds into the Asset tree.</summary>
public sealed class ConnectionImportService(
    ConnectionService connections,
    IAssetRepository assets,
    IRequestAccessRepository access)
{
    /// <summary>The neutral types imported objects use, so existing logic that asks for a "well" keeps working.</summary>
    public const string WellTypeKey = "well";

    public const string GroupTypeKey = "group";

    private const int ConflictsShown = 5;

    public async Task<ImportPreview> PreviewAsync(Guid connectionId, IReadOnlyList<string>? requestedLevels,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        // Imported Assets are created under groups the importer must be able to see and manage afterwards, so for now
        // only someone who manages every Asset may import. The write itself re-checks this inside its transaction.
        if (!await access.HasAllAssetPermissionAsync(subject, WellSitePermissions.AssetsManage, cancellationToken))
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportNeedsAllAssets, FailureKind.Authorization,
                "Importing creates Assets, so it needs permission to manage all Assets. Ask an administrator to import, or to widen your access.");

        var connection = await connections.GetAsync(connectionId, cancellationToken);
        var descriptor = (await connections.ListAdaptersAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, connection.AdapterKey, StringComparison.Ordinal))
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.AdapterUnknown, FailureKind.Conflict,
                "This build no longer includes the adapter this connection uses.");
        var fields = descriptor.GroupingFields ?? [];
        var levels = ValidateLevels(requestedLevels, fields);

        var found = await connections.DiscoverAsync(connectionId, subject, actor, cancellationToken);
        if (!found.Complete)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryIncomplete, FailureKind.Conflict,
                "The system did not give a complete answer, so nothing can be planned from it. " + string.Join(" ", found.Warnings));

        var warnings = found.Warnings.ToList();
        var items = found.Items.GroupBy(item => item.ExternalId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (items.Length < found.Items.Count)
            warnings.Add($"{found.Items.Count - items.Length} object(s) were listed more than once and are counted once.");

        var plan = ImportPlanner.Plan(connectionId, items, levels, fields);
        var existing = (await assets.ListAssetTypesAsync(cancellationToken)).ToDictionary(type => type.Key, StringComparer.Ordinal);
        var typesToCreate = new List<string>();
        var conflicts = new List<string>();
        if (plan.Placements.Count > 0) await CheckTypeAsync(WellTypeKey, existing, typesToCreate, conflicts,
            plan.Placements.Select(item => (item.ExternalId, item.Attributes)), cancellationToken);
        if (plan.GroupCount > 0) await CheckTypeAsync(GroupTypeKey, existing, typesToCreate, conflicts,
            [("group", new Dictionary<string, string>())], cancellationToken);

        var canCreateTypes = typesToCreate.Count == 0 ||
            (await access.GetPermissionHintsAsync(subject, cancellationToken)).Contains(WellSitePermissions.AssetTypesManage);
        if (!canCreateTypes)
            warnings.Add("Importing needs to create the asset types " + string.Join(" and ", typesToCreate) +
                ", which needs permission to manage asset types.");
        return new ImportPreview(plan, warnings, conflicts, typesToCreate, canCreateTypes && conflicts.Count == 0, found.ActingAs);
    }

    private static List<string> ValidateLevels(IReadOnlyList<string>? requested, IReadOnlyList<DiscoveryField> fields)
    {
        var levels = (requested ?? []).Select(level => (level ?? string.Empty).Trim()).ToList();
        var known = fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        if (levels.Count > ImportPlanner.MaximumLevels || levels.Distinct(StringComparer.Ordinal).Count() != levels.Count ||
            levels.Any(level => !known.Contains(level)))
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportLevelsInvalid, FailureKind.Validation,
                $"Choose up to {ImportPlanner.MaximumLevels} different grouping levels from the ones this system offers.");
        return levels;
    }

    private static async Task CheckTypeAsync(string key, Dictionary<string, AssetTypeDefinition> existing,
        List<string> typesToCreate, List<string> conflicts, IEnumerable<(string ExternalId, IReadOnlyDictionary<string, string> Attributes)> samples,
        CancellationToken cancellationToken)
    {
        if (!existing.TryGetValue(key, out var type))
        {
            typesToCreate.Add(key);
            return;
        }

        if (!type.IsActive)
        {
            conflicts.Add($"The asset type \u201c{key}\u201d exists but is not active, so nothing can be created with it.");
            return;
        }

        // Each distinct set of values is checked once; thousands of wells usually share a few shapes.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reported = 0;
        foreach (var (externalId, attributes) in samples)
        {
            var json = JsonSerializer.Serialize(attributes);
            if (!seen.Add(json)) continue;
            try
            {
                await AssetAttributeSchemaValidator.ValidateValuesAsync(type.AttributeSchemaJson, json, cancellationToken);
            }
            catch (WellSiteAutoPilotException)
            {
                if (reported++ < ConflictsShown)
                    conflicts.Add($"The existing asset type \u201c{key}\u201d does not accept the values for {externalId}.");
            }
        }
    }
}
