using WellSiteAutoPilot.Application.Connections.Discovery;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

HierarchyChecks.Verify();
SourceChangeChecks.Verify();
ConnectionMonitorChecks.Verify();
ConnectionCommandCatalogServiceChecks.Verify();

// The planner is pure, so these checks need no database or network.
var connection = Guid.Parse("00000000-0000-4000-8000-000000000001");
var fields = ForeSiteAdapter.Descriptor.GroupingFields!;

DiscoveredAsset Well(string id, string name, params (string Key, string Value)[] facts) =>
    new(id, name, ForeSiteAdapter.WellResourceKind, null, facts.ToDictionary(fact => fact.Key, fact => fact.Value));

var wells = new[]
{
    Well("3", "Bravo 1", ("assetName", "South"), ("wellType", "RRL"), ("field", "Bravo"), ("lease", "L-2")),
    Well("1", "Alpha 1", ("assetName", "North"), ("wellType", "RRL"), ("field", "Alpha"), ("lease", "L-1"), ("deviceType", "RPC")),
    Well("2", "Alpha 2", ("assetName", "North"), ("wellType", "ESP"), ("field", "Alpha")),
    Well("4", "Orphan", ("field", " ")),
};

string[] both = ["assetName", "wellType"];
string[] justAsset = ["assetName"];
string[] justField = ["field"];
string[] none = [];
var plan = ImportPlanner.Plan(connection, wells, both, fields);
Expect(Join(plan.Placements.Select(item => item.ExternalId)) == "1,2,3,4", "placements are ordered by external id");
Expect(Join(plan.Groups.Select(item => item.Name)) == "North,South,Unassigned", "groups sort by name: " + Join(plan.Groups.Select(item => item.Name)));
Expect(plan.GroupCount == 7, "three top groups and four type groups: " + plan.GroupCount);
Expect(plan.FallbackObjectCount == 1, "one object used a fallback");
var north = plan.Groups.Single(item => item.Name == "North");
Expect(north.ObjectCount == 2 && Join(north.Children.Select(item => item.Name)) == "ESP,RRL", "North holds two wells under ESP and RRL");
Expect(Join(north.Children.Single(item => item.Name == "RRL").SampleNames) == "Alpha 1", "samples are the wells held directly in the group");
var unassigned = plan.Groups.Single(item => item.Name == "Unassigned");
Expect(unassigned.IsFallback && unassigned.Children.Single().Name == "Unknown type" && unassigned.Children.Single().IsFallback,
    "missing values land in labelled fallback groups");
Expect(!north.IsFallback, "a real group is not a fallback");

var alpha1 = plan.Placements.Single(item => item.ExternalId == "1");
Expect(Join(alpha1.GroupPath) == "North,RRL" && alpha1.Name == "Alpha 1", "path and name");
Expect(Join(alpha1.Attributes.Keys) == "deviceType,field,lease,wellType",
    "facts people filter on become attributes; the ForeSite asset only shapes the tree: " + Join(alpha1.Attributes.Keys));
Expect(!plan.Placements.Single(item => item.ExternalId == "4").Attributes.ContainsKey("field"), "blank values are not attributes");

var again = ImportPlanner.Plan(connection, wells.Reverse().ToArray(), both, fields);
Expect(again.Fingerprint == plan.Fingerprint, "the fingerprint does not depend on the order the source listed objects");
Expect(ImportPlanner.Plan(connection, wells, justAsset, fields).Fingerprint != plan.Fingerprint, "other levels, other fingerprint");
Expect(ImportPlanner.Plan(Guid.NewGuid(), wells, both, fields).Fingerprint != plan.Fingerprint, "other connection, other fingerprint");
var renamed = wells.Select(item => item.ExternalId == "1" ? item with { DisplayName = "Alpha One" } : item).ToArray();
Expect(ImportPlanner.Plan(connection, renamed, both, fields).Fingerprint != plan.Fingerprint, "a changed name changes the fingerprint");
var moved = wells.Select(item => item.ExternalId == "1"
    ? item with { Metadata = new Dictionary<string, string>(item.Metadata) { ["assetName"] = "South" } } : item).ToArray();
Expect(ImportPlanner.Plan(connection, moved, both, fields).Fingerprint != plan.Fingerprint, "a moved well changes the fingerprint");

var flat = ImportPlanner.Plan(connection, wells, none, fields);
Expect(flat.Groups.Count == 0 && flat.GroupCount == 0 && flat.RootObjectCount == 4 && flat.FallbackObjectCount == 0, "no levels: wells sit at the top");

var empty = ImportPlanner.Plan(connection, [], justField, fields);
Expect(empty.Placements.Count == 0 && empty.Groups.Count == 0, "an empty source plans nothing");

var unnamed = ImportPlanner.Plan(connection, [Well("9", "  ")], none, fields);
Expect(unnamed.Placements[0].Name == "Object 9", "a blank name falls back to the id");
var long256 = ImportPlanner.Plan(connection, [Well("8", new string('x', 400), ("field", new string('y', 400)))], justField, fields);
Expect(long256.Placements[0].Name.Length == 256 && long256.Groups[0].Name.Length == 256, "names are cut to the Asset name limit");

var generic = ImportPlanner.FallbackName(new DiscoveryField("k", "Some fact"));
Expect(generic == "Unspecified: Some fact", "a field without its own label gets a generic one");

// Scope: words, facets and the counts a person sees before choosing.
string[] alphaOnly = ["Alpha"];
string[] espOrRrl = ["ESP", "RRL"];
string[] noLease = ["Unspecified lease"];
string[] anything = ["x"];
var all = DiscoveryScopes.Apply(wells, DiscoveryScope.Everything, fields);
Expect(all.Count == 4, "no scope keeps everything");
Expect(Join(DiscoveryScopes.Apply(wells, new DiscoveryScope("alpha", null), fields).Select(item => item.ExternalId)) == "1,2",
    "a word matches names and facts regardless of case, in the order found");
Expect(DiscoveryScopes.Apply(wells, new DiscoveryScope("alpha rrl", null), fields).Count == 1, "every word must match");
var byField = new Dictionary<string, IReadOnlyList<string>> { ["field"] = alphaOnly, ["wellType"] = espOrRrl };
Expect(Join(DiscoveryScopes.Apply(wells, new DiscoveryScope(null, byField), fields).Select(item => item.ExternalId)) == "1,2",
    "any chosen value within a fact, all facts together");
var unspecified = new Dictionary<string, IReadOnlyList<string>> { ["lease"] = noLease };
Expect(DiscoveryScopes.Apply(wells, new DiscoveryScope(null, unspecified), fields).Count == 2,
    "objects with no value are chosen by the name their group would have");
Expect(DiscoveryScopes.Apply(wells, new DiscoveryScope(null, new Dictionary<string, IReadOnlyList<string>>()), fields).Count == 4,
    "an empty facet choice is no scope");
var unknown = false;
try { DiscoveryScopes.Apply(wells, new DiscoveryScope(null, new Dictionary<string, IReadOnlyList<string>> { ["nonsense"] = anything }), fields); }
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException) { unknown = true; }
Expect(unknown, "a fact the system does not offer is refused");
var tooMany = false;
try { DiscoveryScopes.Apply(wells, new DiscoveryScope(string.Join(' ', Enumerable.Range(0, 11).Select(n => "w" + n)), null), fields); }
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException) { tooMany = true; }
Expect(tooMany, "too many search words are refused");

var facets = DiscoveryScopes.Summarize(wells, fields);
var fieldFacet = facets.Single(facet => facet.Key == "field");
Expect(Join(fieldFacet.Values.Select(value => value.Value + ":" + value.Count)) == "Alpha:2,Bravo:1,Unspecified field:1" && !fieldFacet.HasMore,
    "counts per value, most common first, blanks under their group name");
var many = Enumerable.Range(0, 250).Select(n => Well("m" + n, "M" + n, ("field", "F" + n.ToString("000", System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
var cut = DiscoveryScopes.Summarize(many, fields).Single(facet => facet.Key == "field");
Expect(cut.Values.Count == DiscoveryScopes.MaximumFacetValues && cut.HasMore, "a long list of values is cut and says so");

// Explicit source selections never broaden into a filter or an incomplete replacement.
var chosenObjects = new DiscoveryScope(null, null, [new DiscoveryObjectKey(ForeSiteAdapter.WellResourceKind, "1"),
    new DiscoveryObjectKey(ForeSiteAdapter.WellResourceKind, "4")]);
Expect(Join(DiscoveryScopes.Apply(wells, chosenObjects, fields).Select(item => item.ExternalId)) == "1,4", "explicit wells across branches selected by source identity");
Expect(DiscoveryScopes.Apply(wells, new DiscoveryScope(null, null, []), fields).Count == 0, "an empty explicit set never means all wells");
foreach (var invalid in new[] {
    chosenObjects with { Query = "alpha" },
    chosenObjects with { Facets = new Dictionary<string, IReadOnlyList<string>> { ["wellType"] = ["ESP"] } },
    new DiscoveryScope(null, null, [new DiscoveryObjectKey("foreign.kind", "1")]),
    new DiscoveryScope(null, null, [new DiscoveryObjectKey(ForeSiteAdapter.WellResourceKind, "1"), new DiscoveryObjectKey(ForeSiteAdapter.WellResourceKind, "1")]),
    new DiscoveryScope(null, null, [null!]),
    new DiscoveryScope(null, null, Enumerable.Range(0, 5001).Select(i => new DiscoveryObjectKey("well", i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray()) })
{
    try { DiscoveryScopes.Apply(wells, invalid, fields); throw new InvalidOperationException("Invalid explicit selection was accepted."); }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException error) when (error.Code == "VALIDATION_FAILED") { }
}
Expect(fieldFacet.KnownCount == 3 && facets.Single(facet => facet.Key == "deviceType").KnownCount == 1, "availability counts derive from the full inventory, independently of labels");
var largeInventory = Enumerable.Range(1, 10_000).Select(i => Well(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Same name")).ToArray();
var firstPage = DiscoveryScopes.Page(largeInventory.Reverse().ToArray(), 0, 1000);
var nextPage = DiscoveryScopes.Page(largeInventory, 1000, 100);
Expect(firstPage.Count == 1000 && nextPage.Count == 100 && !firstPage.Select(item => item.ExternalId).Intersect(nextPage.Select(item => item.ExternalId)).Any(), "stable paging reaches beyond the first 1,000 without overlaps");
Expect(DiscoveryScopes.Page(largeInventory, 10000, 100).Count == 0, "end page is empty");
foreach (var (offset, limit) in new[] { (-1, 100), (0, 0), (0, 1001) })
{
    try { DiscoveryScopes.Page(wells, offset, limit); throw new InvalidOperationException("Invalid page was accepted."); }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException error) when (error.Code == "VALIDATION_FAILED") { }
}

// Snapshots: kept briefly, only for the person and connection that found them, replaced by a new find.
var clock = new ManualClock(DateTimeOffset.Parse("2026-10-21T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
var store = new DiscoverySnapshotStore(clock);
var me = Guid.NewGuid();
var other = Guid.NewGuid();
var result = new DiscoveryResult(true, wells, [], "WFT\\svc");
var first = store.Add(connection, me, result, fields);
Expect(store.Find(first.Id, connection, me) is not null, "the finder can read it back");
Expect(store.Find(first.Id, connection, other) is null, "another person cannot");
Expect(store.Find(first.Id, Guid.NewGuid(), me) is null, "another connection cannot");
clock.Advance(DiscoverySnapshotStore.Lifetime - TimeSpan.FromSeconds(1));
Expect(store.Find(first.Id, connection, me) is not null, "it lasts its whole lifetime");
clock.Advance(TimeSpan.FromSeconds(2));
Expect(store.Find(first.Id, connection, me) is null, "it expires after its lifetime");
var second = store.Add(connection, me, result, fields);
var third = store.Add(connection, me, result, fields);
Expect(store.Find(second.Id, connection, me) is null && store.Find(third.Id, connection, me) is not null, "a new find replaces the old one");
var held = new List<DiscoverySnapshot>();
for (var n = 0; n < DiscoverySnapshotStore.MaximumSnapshots + 5; n++)
{
    held.Add(store.Add(Guid.NewGuid(), Guid.NewGuid(), result, fields));
    clock.Advance(TimeSpan.FromSeconds(1));
}
Expect(held.Count(snapshot => store.Find(snapshot.Id, snapshot.ConnectionId, snapshot.UserId) is not null) <= DiscoverySnapshotStore.MaximumSnapshots,
    "only a bounded number are held, oldest first out");
Expect(store.Find(held[^1].Id, held[^1].ConnectionId, held[^1].UserId) is not null, "the newest survives");

Console.WriteLine("Connection import planner checks passed.");
return 0;

static string Join(IEnumerable<string> values) => string.Join(",", values);

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Import planner check failed: " + message + ".");
}

sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
