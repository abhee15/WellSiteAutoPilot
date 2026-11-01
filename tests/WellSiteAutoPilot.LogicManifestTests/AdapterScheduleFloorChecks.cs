using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class AdapterScheduleFloorChecks
{
    private static AdapterDescriptor Adapter(string key, int floor) =>
        new(key, key, key, 1, [], [], null, floor);

    public static void Run()
    {
        const string withAdapter = """{"moduleId":"a.b","requiredAdapterKey":"foresite"}""";
        const string withoutAdapter = """{"moduleId":"a.b"}""";

        if (AdapterScheduleFloorPolicy.RequiredAdapterKey(withAdapter) != "foresite" ||
            AdapterScheduleFloorPolicy.RequiredAdapterKey("""{"RequiredAdapterKey":"x"}""") != "x" ||
            AdapterScheduleFloorPolicy.RequiredAdapterKey(withoutAdapter) is not null ||
            AdapterScheduleFloorPolicy.RequiredAdapterKey("""{"requiredAdapterKey":7}""") is not null ||
            AdapterScheduleFloorPolicy.RequiredAdapterKey("not json") is not null ||
            AdapterScheduleFloorPolicy.RequiredAdapterKey("[]") is not null)
            throw new InvalidOperationException("The module's required adapter was not read exactly.");

        var cache = new AdapterScheduleFloorCache();
        if (cache.MinimumSeconds("foresite") is not null ||
            AdapterScheduleFloorPolicy.FloorFor(withAdapter, cache) is not null)
            throw new InvalidOperationException("A floor was reported before any catalogue arrived.");

        cache.Replace([Adapter("foresite", 120), Adapter("simulator", 0), Adapter("negative", -5)]);
        if (cache.MinimumSeconds("foresite") != 120 || cache.MinimumSeconds("simulator") != 0 ||
            cache.MinimumSeconds("negative") != 0 || cache.MinimumSeconds("unlisted") is not null)
            throw new InvalidOperationException("The catalogue floors were not kept as listed.");

        if (AdapterScheduleFloorPolicy.FloorFor(withAdapter, cache) != 120 ||
            AdapterScheduleFloorPolicy.FloorFor("""{"requiredAdapterKey":"simulator"}""", cache) is not null ||
            AdapterScheduleFloorPolicy.FloorFor(withoutAdapter, cache) is not null ||
            AdapterScheduleFloorPolicy.FloorFor(withAdapter, null) is not null)
            throw new InvalidOperationException("The effective floor was not decided from the module's adapter.");

        cache.Replace([Adapter("simulator", 0)]);
        if (cache.MinimumSeconds("foresite") is not null)
            throw new InvalidOperationException("An adapter the Gateway stopped listing kept its old floor.");
    }
}
