using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CygNet.API.Core;
using CygNet.API.Points;
using CygNet.Data.Core;

// CygNet loads its mixed-mode "*64" assemblies by name; they are not in deps.json, so probe the app folder.
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(e.Name).Name + ".dll");
    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
};

// Usage:
//   Probe <domain> <site>                         service availability
//   Probe facs <domain> <site> <service> [max]    list facilities
//   Probe pts <domain> <site> <service> <facId>   list points + current values for a facility
if (args.Length > 0 && args[0] == "facs") { Facilities(args); return; }
if (args.Length > 0 && args[0] == "pts") { Points(args); return; }
Availability(args);

static void Availability(string[] args)
{
    ushort domain = ushort.Parse(args.Length > 0 ? args[0] : "27212");
    string site = args.Length > 1 ? args[1] : "CYG_DEMO";
    var info = new ServiceInformation();
    try { Console.WriteLine($"Ambient domain: {info.GetAmbientDomain()}"); }
    catch (Exception e) { Console.WriteLine($"Ambient domain failed: {e}"); }

    foreach (var svc in new[] { "UIS", "VHS", "HSS", "CVS", "PNT", "FAC", "DDS" })
    {
        try
        {
            var dss = new DomainSiteService(domain, site, svc);
            Console.WriteLine($"{dss}: uptime {info.GetServiceUpTime(dss)}");
        }
        catch (Exception e) { Console.WriteLine($"{svc}: {e.GetType().Name}: {e.Message}"); }
    }
}

static void Facilities(string[] args)
{
    ushort domain = ushort.Parse(args[1]);
    string site = args[2], service = args[3];
    int max = args.Length > 4 ? int.Parse(args[4]) : 25;
    var info = new ServiceInformation();
    var uis = new DomainSiteService(domain, site, service);
    var fac = info.GetAssociatedService(uis, ServiceType.FAC);
    var client = new CygNet.API.Facilities.Client(fac);
    var tags = client.GetFacilityTagList($"facility_site = '{site}' AND facility_service = '{service}'", CancellationToken.None);
    Console.WriteLine($"{tags.Count} facilities in {site}.{service}");
    foreach (var t in tags.Take(max))
    {
        var r = client.ReadFacility(t);
        Console.WriteLine($"{t.FacilityId}\ttype={r.Type}\t{r.Description}");
    }
}

static void Points(string[] args)
{
    ushort domain = ushort.Parse(args[1]);
    string site = args[2], service = args[3], facId = args[4];
    var info = new ServiceInformation();
    var uis = new DomainSiteService(domain, site, service);
    var pnt = info.GetAssociatedService(uis, ServiceType.PNT);
    var cfg = new ConfigClient(pnt);
    var tags = cfg.GetPointTagList($"siteservice = '{site}.{service}' AND facilityid = '{facId}'", CancellationToken.None);
    Console.WriteLine($"{tags.Count} points on {facId}");
    var rt = new RealtimeClient(uis);
    foreach (var t in tags)
    {
        try
        {
            var v = rt.GetPointValueRecord(t);
            Console.WriteLine($"{t}\t{v?.Value}\t{v?.Timestamp}\tstatus={v?.Status}");
        }
        catch (Exception e) { Console.WriteLine($"{t}\t{e.Message}"); }
    }
}
