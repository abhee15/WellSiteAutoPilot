using System.Reflection;
using CygNet.API.Core;
using CygNet.API.Facilities;
using CygNet.API.Facilities.Relative;
using CygNet.Data.Core;

// Read-only. Usage:
//   defs <domain>                                  dump relative facility definition sets and relation names
//   resolve <domain> <site> <uis> <facilityId> [relation ...]   resolve relations (all names when none given)
//   attrs <domain> <site> <uis> <facilityId>       facility record and attribute values
//   attrdefs <domain> <site> <uis>                 facility attribute definitions
//   linked <domain> <site> <uis> <facilityId>      FAC GetLinkedDevices
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(e.Name).Name + ".dll");
    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
};

var mode = args.Length > 0 ? args[0] : "defs";
var domain = ushort.Parse(args[1]);

RelativeFacilityDefinitionSets Sets()
{
    var sets = new RelativeFacilityDefinitionSets();
    sets.LoadFromDomain(domain);
    return sets;
}

DomainSiteService FacService(string site, string uis) =>
    new ServiceInformation().GetAssociatedService(new DomainSiteService(domain, site, uis), ServiceType.FAC);

Client Fac(string site, string uis) => new(FacService(site, uis));

switch (mode)
{
    case "defs":
    {
        var sets = Sets();
        Console.WriteLine($"DefaultSet={sets.DefaultSet}");
        Console.WriteLine($"AvailableServices={string.Join(", ", sets.GetAvailableServices(domain))}");
        foreach (var name in sets.GetDefinitionNames())
        {
            var set = sets.GetDefinitionSetByName(name);
            Console.WriteLine($"== set '{name}' default={set.Default} parentLink={set.ParentLinkAttribute} pathFormat={set.RelativePathFormat}");
            Console.WriteLine($"   services: {set.AssociatedServicesString}");
            foreach (var d in set.Definitions)
            {
                Console.WriteLine($"   relation '{d.Name}' filter='{d.Filter}' parent='{d.ParentDefinition}' orderAttr='{d.OrderInTypeAttribute}'");
                Console.WriteLine($"      links: {string.Join(" | ", d.Links)}");
                Console.WriteLine($"      simple: {string.Join(" | ", d.AllSimpleLinks.Select(p => p.Key + "=" + p.Value))}");
            }
        }
        Console.WriteLine("---- XML ----");
        Console.WriteLine(sets.ToXml());
        break;
    }
    case "resolve":
    {
        var tag = new FacilityTag($"{args[2]}.{args[3]}::{args[4]}");
        var client = Fac(args[2], args[3]);
        var fac = FacService(args[2], args[3]);
        var set = Sets().GetDefinitionSetByFacilityService(fac.SiteService.ToString());
        Console.WriteLine($"set={set?.Name} (fac service {fac.SiteService})");
        var names = args.Length > 5 ? args.Skip(5) : set!.Definitions.Select(d => d.Name);
        foreach (var rel in names)
        {
            try
            {
                var r = set!.ResolveRelativeFacility(tag, rel, client);
                Console.WriteLine($"{tag.FacilityId} --{rel}--> {(r == null ? "(null)" : r.ToString())}");
            }
            catch (Exception ex) { Console.WriteLine($"{tag.FacilityId} --{rel}--> ERROR {ex.GetType().Name}: {ex.Message}"); }
        }
        break;
    }
    case "attrdefs":
    {
        foreach (var d in Fac(args[2], args[3]).GetAttributeDefinitions().Where(d => d.InUse))
            Console.WriteLine($"{d.ColumnId}\t{d.ColumnName}\t{d.Description}\t{d.Category}");
        break;
    }
    case "attrs":
    {
        var tag = new FacilityTag($"{args[2]}.{args[3]}::{args[4]}");
        var client = Fac(args[2], args[3]);
        var rec = client.ReadFacility(tag);
        Console.WriteLine($"{rec.Tag} type={rec.Type} category={rec.Category} desc={rec.Description} active={rec.IsActive}");
        foreach (var d in client.GetAttributeDefinitions().Where(d => d.InUse))
        {
            string? v;
            try { v = rec.GetNamedProperty(d.ColumnName)?.ToString(); } catch (Exception ex) { v = "ERR " + ex.Message; }
            if (!string.IsNullOrWhiteSpace(v)) Console.WriteLine($"  {d.ColumnName} ({d.Description}) = {v}");
        }
        break;
    }
    case "scan":
    {
        // Tallies type/category and every in-use attribute column over all facilities of the site.service.
        var client = Fac(args[2], args[3]);
        var tags = client.GetFacilityTagList($"facility_site = '{args[2]}' AND facility_service = '{args[3]}'", CancellationToken.None);
        var defs = client.GetAttributeDefinitions().Where(d => d.InUse && d.ColumnId > 15).ToList();
        var types = new SortedDictionary<string, int>();
        var filled = defs.ToDictionary(d => d.ColumnName, _ => new List<string>());
        foreach (var chunk in tags.Chunk(250))
            foreach (var rec in client.ReadFacilityRecords(chunk, CancellationToken.None))
            {
                var key = $"{rec.Type}/{rec.Category}";
                types[key] = types.GetValueOrDefault(key) + 1;
                foreach (var d in defs)
                {
                    var v = rec.GetNamedProperty(d.ColumnName)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) filled[d.ColumnName].Add($"{rec.Tag.FacilityId}={v}");
                }
            }
        Console.WriteLine($"{tags.Count} facilities");
        foreach (var (k, n) in types) Console.WriteLine($"  {k}: {n}");
        foreach (var (k, list) in filled)
            Console.WriteLine($"{k}: {list.Count} filled; sample: {string.Join(", ", list.Take(12))}");
        break;
    }
    case "tree":
    {
        // Parent Facility (facility_attr22) hierarchy: roots, depth, children by type.
        var client = Fac(args[2], args[3]);
        var tags = client.GetFacilityTagList($"facility_site = '{args[2]}' AND facility_service = '{args[3]}'", CancellationToken.None);
        var recs = new Dictionary<string, (string Type, string? Parent)>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in tags.Chunk(250))
            foreach (var rec in client.ReadFacilityRecords(chunk, CancellationToken.None))
                recs[rec.Tag.ToString()] = (rec.Type, rec.GetNamedProperty("facility_attr22")?.ToString());
        var withParent = recs.Where(r => !string.IsNullOrWhiteSpace(r.Value.Parent)).ToList();
        Console.WriteLine($"{withParent.Count} facilities have a parent; {withParent.Count(r => !recs.ContainsKey(r.Value.Parent!))} point at a missing facility");
        int Depth(string k) { var d = 0; while (recs.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v.Parent) && d < 50) { k = v.Parent!; d++; } return d; }
        foreach (var g in withParent.GroupBy(r => Depth(r.Key)).OrderBy(g => g.Key)) Console.WriteLine($"depth {g.Key}: {g.Count()}");
        foreach (var g in withParent.GroupBy(r => $"{recs.GetValueOrDefault(r.Value.Parent!).Type ?? "?"} <- {r.Value.Type}").OrderBy(g => g.Key))
            Console.WriteLine($"  parent<-child {g.Key}: {g.Count()}");
        foreach (var g in withParent.GroupBy(r => r.Value.Parent!).OrderByDescending(g => g.Count()).Take(8))
            Console.WriteLine($"  {g.Key} ({recs.GetValueOrDefault(g.Key).Type}) parent of {g.Count()}: {string.Join(", ", g.Take(4).Select(x => x.Key.Split("::")[1]))}");
        var withKids = withParent.Select(r => r.Value.Parent!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in withKids.Where(k => !recs.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v.Parent)))
            Console.WriteLine($"  root {root} type={recs.GetValueOrDefault(root).Type}");
        break;
    }
    case "synth":
    {
        // Experiments with an in-memory definition set: args = site uis facilityId parentAttr
        var client = Fac(args[2], args[3]);
        var tag = new FacilityTag($"{args[2]}.{args[3]}::{args[4]}");
        var set = new RelativeFacilityDefinitions { Name = "Probe", ParentLinkAttribute = args[5] };
        set.AddAssociatedService($"{args[2]}.FAC");
        set.AddDefinition(new RelativeFacilityDefinition { Name = "GasMeter", OrderInTypeAttribute = "facility_type", Filter = string.Format(args[6], "GASMETER") });
        set.AddDefinition(new RelativeFacilityDefinition { Name = "OilMeter", OrderInTypeAttribute = "facility_type", Filter = string.Format(args[6], "OILMETER") });
        set.AddDefinition(new RelativeFacilityDefinition { Name = "Parent", Filter = "" });
        Console.WriteLine(set.ToXml());
        foreach (var d in set.Definitions)
            Console.WriteLine($"{d.Name}: links=[{string.Join(",", d.Links)}] avail=[{string.Join(",", d.AvailableLinks)}] paths=[{string.Join(",", set.GetAllPaths(d.Name))}]");
        foreach (var rel in new[] { "GasMeter", "OilMeter", "Parent" })
            try { Console.WriteLine($"{rel} -> {set.ResolveRelativeFacility(tag, rel, client)?.ToString() ?? "(null)"}"); }
            catch (Exception ex) { Console.WriteLine($"{rel} ERROR {ex.GetType().Name}: {ex.Message}"); }
        set.AddDefinition(new RelativeFacilityDefinition { Name = "Well", Filter = string.Format(args[6], "WELL") });
        foreach (var (a, b) in new[] { ("Well", "GasMeter"), ("Well", "OilMeter") })
            Console.WriteLine($"AddLink({a},{b}) = {set.AddLink(a, b)}");
        Console.WriteLine(set.ToXml());
        foreach (var d in set.Definitions)
            Console.WriteLine($"{d.Name}: links=[{string.Join(",", d.Links)}] avail=[{string.Join(",", d.AvailableLinks)}] simple=[{string.Join(",", d.AllSimpleLinks.Select(p => p.Key + "=" + p.Value))}] paths=[{string.Join(",", set.GetAllPaths(d.Name))}]");
        foreach (var rel in new[] { "GasMeter", "OilMeter", "Well", "Parent" })
            try { Console.WriteLine($"4arg Well,{rel} -> {set.ResolveRelativeFacility(tag, "Well", rel, client)?.ToString() ?? "(null)"}"); }
            catch (Exception ex) { Console.WriteLine($"4arg {rel} ERROR {ex.GetType().Name}: {ex.Message}"); }
        foreach (var rel in new[] { "GasMeter", "OilMeter", "Well", "Parent" })
            try { Console.WriteLine($"{rel} -> {set.ResolveRelativeFacility(tag, rel, client)?.ToString() ?? "(null)"}"); }
            catch (Exception ex) { Console.WriteLine($"{rel} ERROR {ex.GetType().Name}: {ex.Message}"); }
        break;
    }
    case "linked":
    {
        var tag = new FacilityTag($"{args[2]}.{args[3]}::{args[4]}");
        foreach (var l in Fac(args[2], args[3]).GetLinkedDevices(tag)) Console.WriteLine(l);
        break;
    }
}
