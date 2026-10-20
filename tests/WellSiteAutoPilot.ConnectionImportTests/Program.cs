using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

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

Console.WriteLine("Connection import planner checks passed.");
return 0;

static string Join(IEnumerable<string> values) => string.Join(",", values);

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Import planner check failed: " + message + ".");
}
