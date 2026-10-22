using System.Text.Json;
using System.Security.Cryptography;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections.Discovery;
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
    string? ActingAs,
    int AlreadyImported,
    int ConnectionRevision, ImportHierarchyState Hierarchy, bool AdoptingHierarchy, int GroupsToAdopt,
    IReadOnlyList<DiscoveryField> Fields, IReadOnlyList<string>? UnavailableFields);

/// <summary>What an import did. Wells and groups created, and groups that already existed and were reused.</summary>
public sealed record ImportOutcome(int WellsCreated, int GroupsCreated, int GroupsReused, int AlreadyImported,
    IReadOnlyList<string> TypesCreated, bool HierarchyAdopted = false);

/// <summary>
/// What to import from: a snapshot the person already looked at (so review and import see the same list) and the
/// part of it they chose. With no snapshot the connection is read afresh and everything found is used.
/// </summary>
public sealed record ImportSelection(Guid? SnapshotId, DiscoveryScope? Scope);

/// <summary>Plans an import of what a connection holds into the Asset tree.</summary>
public sealed class ConnectionImportService(
    ConnectionService connections,
    IAssetRepository assets,
    AssetService assetService,
    IConnectionImportRepository imports,
    IRequestAccessRepository access)
{
    /// <summary>The neutral types imported objects use, so existing logic that asks for a "well" keeps working.</summary>
    public const string WellTypeKey = "well";

    public const string GroupTypeKey = "group";

    private const int ConflictsShown = 5;

    public async Task<ImportHierarchyStatus> HierarchyStatusAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
        await imports.ReadHierarchyStatusAsync(connectionId, cancellationToken);

    public async Task<ImportPreview> PreviewAsync(Guid connectionId, IReadOnlyList<string>? requestedLevels,
        ImportSelection? selection, AccessSubject subject, SecurityActorContext actor, bool adoptExistingHierarchy = false, CancellationToken cancellationToken = default)
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

        var snapshot = selection?.SnapshotId is { } snapshotId ? connections.GetSnapshot(connectionId, snapshotId, subject) : null;
        if (snapshot is not null && snapshot.ConnectionRevision != connection.Revision)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
                "The Connection changed after discovery. Discover again before importing.");
        var found = snapshot?.Result ?? await connections.DiscoverAsync(connectionId, subject, actor, cancellationToken);
        if ((await connections.GetAsync(connectionId, cancellationToken)).Revision != connection.Revision)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
                "The Connection changed during discovery. Discover again before importing.");
        if (!found.Complete)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryIncomplete, FailureKind.Conflict,
                "The system did not give a complete answer, so nothing can be planned from it. " + string.Join(" ", found.Warnings));

        var warnings = found.Warnings.ToList();
        var chosen = DiscoveryScopes.Apply(found.Items, selection?.Scope, fields);
        if (chosen.Count > ConnectionService.MaximumImportedObjects)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportTooLarge, FailureKind.Validation,
                $"{chosen.Count} objects are selected, but one import takes at most {ConnectionService.MaximumImportedObjects}. " +
                "Narrow the selection by field, lease or search and import the rest afterwards.");
        var distinct = chosen.GroupBy(item => (item.ProviderType, item.ExternalId)).Select(group => group.First()).ToArray();
        if (distinct.Length < chosen.Count)
            warnings.Add($"{chosen.Count - distinct.Length} object(s) were listed more than once and are counted once.");
        // Objects that already have an Asset are left alone: bindings, not names, say what was imported.
        var bound = await imports.BoundKeysAsync(connectionId, cancellationToken);
        var items = distinct.Where(item => !bound.Contains(IConnectionImportRepository.BindingKey(item.ProviderType, item.ExternalId))).ToArray();
        var alreadyImported = distinct.Length - items.Length;

        var hierarchy = await imports.ReadHierarchyAsync(connectionId, cancellationToken);
        var adopting = hierarchy.Status.Status == "Legacy" && adoptExistingHierarchy;
        var hierarchyConflicts = new List<string>();
        var groupsToAdopt = 0;
        if (hierarchy.Levels is { } savedLevels && (!savedLevels.SequenceEqual(levels) || !hierarchy.MatchesFields(levels, fields)))
            hierarchyConflicts.Add("This Connection already has a saved hierarchy. Import using its saved grouping levels; restructuring needs a separate review.");
        if (hierarchy.Status.Status == "Legacy")
        {
            if (!adopting) hierarchyConflicts.Add("Existing imported wells need a reviewed hierarchy adoption before importing more wells.");
            else
            {
                try
                {
                    groupsToAdopt = hierarchy.Adoption(connectionId, levels, fields).Count;
                    if (hierarchy.Groups.Count > groupsToAdopt) warnings.Add(
                        $"{hierarchy.Groups.Count - groupsToAdopt} unused or unverified existing group(s) will be retained unchanged and not reused by name.");
                }
                catch (WellSiteAutoPilotException error) { hierarchyConflicts.Add(error.Message); }
            }
        }
        var unavailable = found.UnavailableFields ?? (found.Warnings.Count > 0 ? fields.Select(field => field.Key).ToArray() : []);
        if (levels.Any(unavailable.Contains)) hierarchyConflicts.Add(
            "Some grouping facts could not be read. Retry discovery before importing with these hierarchy levels.");
        var plan = ImportPlanner.Plan(connectionId, items, levels, fields, hierarchy.StableGroups?.ToDictionary(group => group.Key, group => group.Name, StringComparer.Ordinal));
        plan = plan with { Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            plan.Fingerprint, hierarchy = hierarchy.Fingerprint, adopting, unavailable = unavailable.Order(StringComparer.Ordinal), found.ActingAs }))) };
        var indexedGroups = hierarchy.StableGroups?.ToDictionary(group => group.Key, StringComparer.Ordinal);
        if (indexedGroups is not null)
        {
            foreach (var placement in plan.Placements)
            {
                Guid? parentId = null;
                var newParent = false;
                foreach (var key in placement.GroupKeys)
                {
                    if (indexedGroups.TryGetValue(key, out var group))
                    {
                        if (newParent || !group.IsActive || group.ParentId != parentId)
                        {
                            hierarchyConflicts.Add("An existing hierarchy group was moved or made inactive. Review that local change before importing beneath it.");
                            break;
                        }
                        parentId = group.AssetId;
                    }
                    else { newParent = true; parentId = null; }
                }
                if (hierarchyConflicts.Count > 0) break;
            }
        }
        var existing = (await assets.ListAssetTypesAsync(cancellationToken)).ToDictionary(type => type.Key, StringComparer.Ordinal);
        var typesToCreate = new List<string>();
        var conflicts = hierarchyConflicts;
        if (plan.Placements.Count > 0 || adopting) await CheckTypeAsync(WellTypeKey, existing, typesToCreate, conflicts,
            plan.Placements.Select(item => (item.ExternalId, item.Attributes)), cancellationToken);
        if (plan.GroupCount > 0 || groupsToAdopt > 0) await CheckTypeAsync(GroupTypeKey, existing, typesToCreate, conflicts,
            [("group", new Dictionary<string, string>())], cancellationToken);

        var canCreateTypes = typesToCreate.Count == 0 ||
            (await access.GetPermissionHintsAsync(subject, cancellationToken)).Contains(WellSitePermissions.AssetTypesManage);
        if (!canCreateTypes)
            warnings.Add("Importing needs to create the asset types " + string.Join(" and ", typesToCreate) +
                ", which needs permission to manage asset types.");
        return new ImportPreview(plan, warnings, conflicts, typesToCreate, canCreateTypes && conflicts.Count == 0, found.ActingAs,
            alreadyImported, connection.Revision, hierarchy, adopting, groupsToAdopt, fields, unavailable);
    }

    /// <summary>
    /// Imports what the reviewed plan showed. The plan is made again from the selected kept snapshot or a fresh source read; if it is not
    /// the plan the person reviewed (<paramref name="expectedFingerprint"/>), nothing is written.
    /// </summary>
    public async Task<ImportOutcome> ImportAsync(Guid connectionId, IReadOnlyList<string>? requestedLevels,
        ImportSelection? selection, string? expectedFingerprint, AccessSubject subject, SecurityActorContext actor,
        bool adoptExistingHierarchy = false, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(connectionId, requestedLevels, selection, subject, actor, adoptExistingHierarchy, cancellationToken);
        if (!string.Equals(preview.Plan.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
                "What the system holds has changed since you reviewed it. Review the import again.");
        if (!preview.CanImport)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportNotAllowed, FailureKind.Conflict,
                "This import cannot go ahead: " + string.Join(" ", preview.Conflicts.Concat(preview.Warnings)));
        if (preview.Plan.Placements.Count == 0 && !preview.AdoptingHierarchy)
            return new ImportOutcome(0, 0, 0, preview.AlreadyImported, []);

        var created = new List<string>();
        foreach (var key in preview.TypesToCreate)
        {
            try
            {
                await assetService.CreateAssetTypeAuthorizedAsync(TypeFor(key, preview.Plan), subject, actor, cancellationToken);
                created.Add(key);
            }
            catch (WellSiteAutoPilotException exception) when (exception.Code == "ASSET_TYPE_KEY_CONFLICT")
            {
                // Someone created the same type a moment ago; it is looked up again below and used as is.
            }
        }

        var types = (await assets.ListAssetTypesAsync(cancellationToken)).ToDictionary(type => type.Key, StringComparer.Ordinal);
        // The Group type is only needed when the plan really has groups; a flat import must not require it.
        var needsGroups = preview.Plan.GroupCount > 0 || preview.GroupsToAdopt > 0;
        if (!types.TryGetValue(WellTypeKey, out var well) || (needsGroups && !types.ContainsKey(GroupTypeKey)))
            throw new WellSiteAutoPilotException("ASSET_TYPE_NOT_FOUND", FailureKind.NotFound,
                "The asset types for this import are unavailable.");
        var groupTypeId = needsGroups ? types[GroupTypeKey].Id : well.Id;
        var applied = await imports.ApplyAuthorizedAsync(
            new ImportApplyRequest(connectionId, preview.Plan, well.Id, groupTypeId, preview.ConnectionRevision,
                preview.Hierarchy.Fingerprint, preview.AdoptingHierarchy, preview.Fields, preview.UnavailableFields, preview.ActingAs),
            subject, actor, cancellationToken);
        return new ImportOutcome(applied.WellsCreated, applied.GroupsCreated, applied.GroupsReused, preview.AlreadyImported, created, applied.HierarchyAdopted);
    }

    // Both types are neutral. The well type declares the facts people filter on; it allows others so a later
    // system can add its own without changing the type.
    private static CreateAssetTypeCommand TypeFor(string key, ImportPlan plan)
    {
        if (key == GroupTypeKey) return new CreateAssetTypeCommand(GroupTypeKey, "Group", """{"type":"object"}""");
        var names = plan.Placements.SelectMany(item => item.Attributes.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var properties = string.Join(",", names.Select(name => $"{JsonSerializer.Serialize(name)}:{{\"type\":\"string\",\"maxLength\":256}}"));
        return new CreateAssetTypeCommand(WellTypeKey, "Well", "{\"type\":\"object\",\"properties\":{" + properties + "}}");
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
