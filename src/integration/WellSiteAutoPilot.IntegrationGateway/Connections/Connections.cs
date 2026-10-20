using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet;
using WellSiteAutoPilot.Integration.ForeSite;

namespace WellSiteAutoPilot.IntegrationGateway.Connections;

/// <summary>POC connection record. Settings never carry secrets; Windows integrated identity is used for ForeSite.</summary>
public sealed record ConnectionRecord(
    Guid Id,
    string Name,
    string AdapterKey,
    string Endpoint,
    bool Enabled,
    int Revision,
    DateTimeOffset UpdatedAtUtc,
    ConnectionTestResult? LastTest,
    Dictionary<string, string>? Settings = null);

public sealed record ConnectionTestResult(
    int TestedRevision,
    DateTimeOffset TestedAtUtc,
    string Health,
    string? HealthReason,
    string Compatibility,
    string? CompatibilityReason,
    bool SupportsDiscovery);

public sealed record AdapterInfo(string Key, string DisplayName, string DefaultEndpoint, bool SupportsDiscovery, string Description, string[]? SettingKeys = null);

public sealed record ConnectionInput(string Name, string AdapterKey, string Endpoint, bool Enabled, int? Revision, Dictionary<string, string>? Settings = null);

public sealed record DiscoveryResult(Guid ConnectionId, DateTimeOffset DiscoveredAtUtc, IReadOnlyCollection<DiscoveredAsset> Assets);

/// <summary>Explicit-code adapter registry: one entry per adapter key, one adapter instance built per saved connection.</summary>
public sealed class AdapterRegistry(IHttpClientFactory clients)
{
    private static readonly AdapterInfo[] Adapters =
    [
        new("foresite", "ForeSite (read-only)", "http://localhost",
            true, "Discovers wells from the ForeSite WellService; health from Administration/ServiceStatus. Reads are scoped to the ForeSite user's asset access."),
        new("simulator", "Provider simulator", "http://127.0.0.1:5091",
            false, "Development simulator. Health only; no asset discovery."),
        new("cygnet", "CygNet (read-only, via CygNet agent)", "http://127.0.0.1:5099",
            true, "Discovers facilities (default type WELL) from a CygNet FAC service through the CygNet Native Agent on a CygNet machine. Windows identity; CygNet permissions apply.",
            ["domain", "site", "service", "facilityType"])
    ];

    public IReadOnlyCollection<AdapterInfo> List() => Adapters;

    public AdapterInfo? Find(string key) => Adapters.FirstOrDefault(a => a.Key == key);

    public (IProviderHealthProvider Health, IProviderMetadataProvider? Metadata, IAssetDiscoveryProvider? Discovery) Build(ConnectionRecord connection)
    {
        var client = clients.CreateClient("connection-probe");
        client.BaseAddress = new Uri(connection.Endpoint.TrimEnd('/') + "/");
        switch (connection.AdapterKey)
        {
            case "foresite":
                var foresite = new ForeSiteAssetDiscoveryProvider(client, "foresite:" + connection.Id);
                return (foresite, foresite, foresite);
            case "cygnet":
                var cygnet = new CygNetFacilityDiscoveryProvider(
                    client, CygNetScope.FromSettings(connection.Settings), "cygnet:" + connection.Id);
                return (cygnet, cygnet, cygnet);
            case "simulator":
                return (new Providers.SimulatorCurrentDataProvider(client), null, null);
            default:
                throw new InvalidOperationException($"Unknown adapter '{connection.AdapterKey}'.");
        }
    }
}

/// <summary>JSON-file store with optimistic revisions. POC stand-in for a persisted Connection entity.</summary>
public sealed class ConnectionStore(string path, TimeProvider time)
{
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private List<ConnectionRecord> Load() =>
        File.Exists(path) ? JsonSerializer.Deserialize<List<ConnectionRecord>>(File.ReadAllText(path), Json) ?? [] : [];

    private void Save(List<ConnectionRecord> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(items, Json));
    }

    public IReadOnlyList<ConnectionRecord> List() { lock (gate) return Load(); }

    public ConnectionRecord? Get(Guid id) { lock (gate) return Load().FirstOrDefault(c => c.Id == id); }

    public ConnectionRecord Create(ConnectionInput input)
    {
        lock (gate)
        {
            var items = Load();
            var created = new ConnectionRecord(Guid.NewGuid(), input.Name.Trim(), input.AdapterKey, input.Endpoint.Trim(),
                input.Enabled, 1, time.GetUtcNow(), null, input.Settings);
            items.Add(created);
            Save(items);
            return created;
        }
    }

    /// <summary>Returns null when missing; throws on a stale revision or an attempt to change the adapter.</summary>
    public ConnectionRecord? Update(Guid id, ConnectionInput input)
    {
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(c => c.Id == id);
            if (index < 0) return null;
            var current = items[index];
            if (input.Revision != current.Revision) throw new StaleRevisionException();
            if (input.AdapterKey != current.AdapterKey) throw new InvalidOperationException("The adapter of a connection cannot be changed.");
            var updated = current with
            {
                Name = input.Name.Trim(), Endpoint = input.Endpoint.Trim(), Enabled = input.Enabled, Settings = input.Settings,
                Revision = current.Revision + 1, UpdatedAtUtc = time.GetUtcNow()
            };
            items[index] = updated;
            Save(items);
            return updated;
        }
    }

    public bool Delete(Guid id)
    {
        lock (gate)
        {
            var items = Load();
            var removed = items.RemoveAll(c => c.Id == id) > 0;
            if (removed) Save(items);
            return removed;
        }
    }

    /// <summary>Stores test evidence only if the connection is still at the tested revision.</summary>
    public ConnectionRecord? RecordTest(Guid id, ConnectionTestResult result)
    {
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(c => c.Id == id);
            if (index < 0 || items[index].Revision != result.TestedRevision) return null;
            items[index] = items[index] with { LastTest = result };
            Save(items);
            return items[index];
        }
    }
}

public sealed class StaleRevisionException : Exception;
