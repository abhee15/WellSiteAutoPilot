using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// Late-bound access to the CygNet API installed on this machine. The libraries are loaded from the folder
/// <see cref="CygNetInstallLocator"/> finds, so the Gateway needs no CygNet files at build time and ships none.
/// Every method is synchronous and can block on the network: call it only through <see cref="CygNetCallGate"/>.
/// </summary>
internal sealed class CygNetRuntime
{
    private static readonly object Gate = new();
    private static CygNetRuntime? current;

    private readonly Type serviceInformation = Find("CygNet.API.Core.ServiceInformation", "CygNet.API.Core");
    private readonly Type domainSiteService = Find("CygNet.Data.Core.DomainSiteService", "CygNet.Data.Core");
    private readonly Type serviceType = Find("CygNet.Data.Core.ServiceType", "CygNet.Data.Core");
    private readonly Type facilityTag = Find("CygNet.Data.Core.FacilityTag", "CygNet.Data.Core");
    private readonly Type pointTag = Find("CygNet.Data.Core.PointTag", "CygNet.Data.Core");
    private readonly Type facilityClient = Find("CygNet.API.Facilities.Client", "CygNet.API.Facilities");
    private readonly Type realtimeClient = Find("CygNet.API.Points.RealtimeClient", "CygNet.API.Points");
    private readonly Type configClient = Find("CygNet.API.Points.ConfigClient", "CygNet.API.Points");
    private readonly Type historianClient = Find("CygNet.API.Historian.Client", "CygNet.API.Historian");
    private readonly Type historianName = Find("CygNet.Data.Historian.Name", "CygNet.Data.Historian");

    private CygNetRuntime(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>The CygNet runtime, or <see cref="CygNetNotInstalledException"/> when this machine has no CygNet API.</summary>
    public static CygNetRuntime Require()
    {
        lock (Gate)
        {
            if (current is not null)
                return current;
            var folder = CygNetInstallLocator.Locate() ?? throw new CygNetNotInstalledException();
            Attach(folder);
            try
            {
                current = new CygNetRuntime(folder);
            }
            catch (Exception error) when (error is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
            {
                throw new CygNetNotInstalledException(folder, error);
            }
            return current;
        }
    }

    // CygNet loads its "*64" assemblies and native Cx*64.dll libraries by name; they are in CygNet's folder, not ours.
    private static void Attach(string folder)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(folder, name.Name + ".dll");
            return name.Name is not null && File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        if (OperatingSystem.IsWindows())
            SetDllDirectory(folder);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    private static Type Find(string typeName, string assemblyName) =>
        Type.GetType($"{typeName}, {assemblyName}", throwOnError: true)!;

    /// <summary>The error a CygNet call raised, without the reflection wrapper.</summary>
    public static Exception Unwrap(Exception error) =>
        error is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : error;

    /// <summary>CygNet reports an unknown domain, an unknown site.service and an unreachable server alike as a messaging failure.</summary>
    public static bool IsMessagingFailure(Exception error)
    {
        for (var type = Unwrap(error).GetType(); type is not null; type = type.BaseType)
            if (type.FullName == "CygNet.Data.Core.MessagingException")
                return true;
        return false;
    }

    public ushort AmbientDomain() => (ushort)((dynamic)Activator.CreateInstance(serviceInformation)!).GetAmbientDomain();

    public object Service(ushort domain, string site, string service) =>
        Activator.CreateInstance(domainSiteService, domain, site, service)!;

    /// <summary>The service of the given type (<c>FAC</c>, <c>VHS</c>) that belongs with the named service.</summary>
    public object AssociatedService(object service, string type) =>
        ((dynamic)Activator.CreateInstance(serviceInformation)!).GetAssociatedService((dynamic)service, (dynamic)Enum.Parse(serviceType, type));

    public dynamic FacilityClient(object facilityService) => Activator.CreateInstance(facilityClient, facilityService)!;

    public dynamic RealtimeClient(object service) => Activator.CreateInstance(realtimeClient, service)!;

    public dynamic HistorianClient(object historianService) => Activator.CreateInstance(historianClient, historianService)!;

    public object FacilityTag(string tag) => Activator.CreateInstance(facilityTag, tag)!;

    /// <summary>The service a facility tag names (its site.service) in the given domain.</summary>
    public object ServiceOf(ushort domain, object facility) =>
        Activator.CreateInstance(domainSiteService, domain, (object)((dynamic)facility).SiteService)!;

    public object PointTag(object facility, string udc) => Activator.CreateInstance(pointTag, facility, udc)!;

    /// <summary>The UDCs of every point CygNet's point service holds for a facility.</summary>
    public IReadOnlyList<string> PointUdcs(ushort domain, string facilityTagText)
    {
        var facility = FacilityTag(facilityTagText);
        var site = (object)((dynamic)facility).SiteService;
        var pnt = AssociatedService(ServiceOf(domain, facility), "PNT");
        var config = Activator.CreateInstance(configClient, pnt)!;
        var filter = $"siteservice = '{site}' AND facilityid = '{((dynamic)facility).FacilityId}'";
        var tags = (IEnumerable)((dynamic)config).GetPointTagList((dynamic)filter, default(CancellationToken));
        return tags.Cast<dynamic>().Select(t => (string)t.UDC).OrderBy(u => u, StringComparer.Ordinal).ToList();
    }

    public dynamic HistorianName(string id)
    {
        dynamic name = Activator.CreateInstance(historianName)!;
        name.ID = id;
        return name;
    }

    /// <summary>A <c>List&lt;FacilityTag&gt;</c>, the collection type the facility client's read methods take.</summary>
    public object FacilityTagList(IEnumerable<object> tags)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(facilityTag))!;
        foreach (var tag in tags)
            list.Add(tag);
        return list;
    }
}
