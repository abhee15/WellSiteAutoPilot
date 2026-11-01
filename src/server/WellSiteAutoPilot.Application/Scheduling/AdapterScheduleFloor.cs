using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>
/// The shortest interval each adapter allows between scheduled reads. Read from memory on the scheduler's path, so a
/// slow or unreachable Gateway never delays dispatch.
/// </summary>
public interface IAdapterScheduleFloors
{
    /// <summary>Seconds, 0 when the adapter sets no floor, or null when the adapter's floor is not known yet.</summary>
    int? MinimumSeconds(string adapterKey);
}

/// <summary>The last catalogue the Gateway answered with. Until one arrives every floor is unknown.</summary>
public sealed class AdapterScheduleFloorCache : IAdapterScheduleFloors
{
    private volatile IReadOnlyDictionary<string, int>? floors;

    public int? MinimumSeconds(string adapterKey) =>
        floors is { } known && known.TryGetValue(adapterKey, out var seconds) ? seconds : null;

    /// <summary>Replaces the whole catalogue, so an adapter the Gateway no longer lists becomes unknown again.</summary>
    public void Replace(IEnumerable<AdapterDescriptor> descriptors) =>
        floors = descriptors.ToDictionary(item => item.Key, item => Math.Max(0, item.MinimumScheduleSeconds), StringComparer.Ordinal);
}

public static class AdapterScheduleFloorPolicy
{
    /// <summary>The adapter a module's manifest requires, or null when it names none or the JSON cannot be read.</summary>
    public static string? RequiredAdapterKey(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "requiredAdapterKey", StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The adapter floor in seconds when it is known and above zero; null when the module uses no adapter, the adapter sets no
    /// floor, or the floor is not known yet (the Server-wide bounds still apply in that case).
    /// </summary>
    public static int? FloorFor(string manifestJson, IAdapterScheduleFloors? floors)
    {
        if (floors is null || RequiredAdapterKey(manifestJson) is not { } key) return null;
        return floors.MinimumSeconds(key) is > 0 and var seconds ? seconds : null;
    }
}
