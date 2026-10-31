using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

public static class CygNetAdapter
{
    public const string Key = "cygnet";
    public const string ResourceKind = "cygnet.facility";
    public const int MaximumFacilityTagLength = 128;
    public const int MaximumFacilities = 5000;
    public const string MappingsSetting = "pointMappings";
    public static AdapterDescriptor Descriptor { get; } = new(
        Key, "CygNet",
        "Reads as the Integration Gateway Windows account, which needs a CygNet licence and domain access. " +
        "Requires a Windows x64 Gateway built with the installed CygNet libraries.", 1,
        [new("domain", "Domain", "CygNet domain number, from 0 to 65535.", AdapterSettingKind.WholeNumber, true, 5),
         new("site", "Site", "Site of the owning service, for example SITE1.", AdapterSettingKind.Text, true, 32),
         new("service", "Owning service", "Service owning the facilities and realtime values, usually UIS. The associated FAC service is resolved automatically.", AdapterSettingKind.Text, true, 32),
         new(MappingsSetting, "Point mappings", "JSON quantity map, for example {\"AveragePumpFillage\":{\"udc\":\"RRLPFILAVG\",\"unit\":\"%\"}}. Confirm point meaning and units at your site.", AdapterSettingKind.Text, true, 16384, IdentifiesSource: false)],
        [new(ProviderCapabilityKind.AssetDiscovery, "Facilities"), new(ProviderCapabilityKind.CurrentData, "Mapped numeric points")],
        [new("facilityType", "Facility type", "Unspecified type")]);

    public static CygNetConnection? ReadConnection(IReadOnlyDictionary<string, string?> settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TryGetValue("domain", out var domain);
        settings.TryGetValue("site", out var site);
        settings.TryGetValue("service", out var service);
        problem = "The domain must be a whole number from 0 to 65535.";
        if (!ushort.TryParse(domain?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null;
        site = site?.Trim(); service = service?.Trim();
        problem = "The site and service must use letters, digits, underscores or hyphens (at most 32 characters).";
        if (!IsName(site, 32) || !IsName(service, 32)) return null;
        problem = null;
        return new(number, site!, service!);
    }

    public static IReadOnlyDictionary<string, CygNetQuantityMapping>? ReadMappings(IReadOnlyDictionary<string, string?> settings)
    {
        if (!settings.TryGetValue(MappingsSetting, out var json) || string.IsNullOrWhiteSpace(json) || json.Length > 16384) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, CygNetQuantityMapping>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateObject())
            {
                if (!IsName(item.Name, 128) || item.Value.ValueKind != JsonValueKind.Object) return null;
                var fields = item.Value.EnumerateObject().ToArray();
                if (fields.Length != 2 || fields.Count(f => f.Name == "udc") != 1 || fields.Count(f => f.Name == "unit") != 1 ||
                    fields.Any(f => f.Value.ValueKind != JsonValueKind.String)) return null;
                var udc = item.Value.GetProperty("udc").GetString();
                var unit = item.Value.GetProperty("unit").GetString();
                if (!IsName(udc, 64) || string.IsNullOrWhiteSpace(unit) || unit.Length > 64 || unit.Any(char.IsControl) ||
                    unit != unit.Trim() || !result.TryAdd(item.Name, new(udc!, unit))) return null;
                if (result.Count > 128) return null;
            }
            return result.Count == 0 ? null : result;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsName(string? value, int maximum) => !string.IsNullOrEmpty(value) && value.Length <= maximum &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static async Task<AdapterTestResult> CheckAsync(ICygNetFacilityReader reader, CygNetConnection connection,
        string actingAs, CancellationToken cancellationToken = default)
    {
        try
        {
            await reader.CheckAsync(connection, cancellationToken);
            return new(AdapterTestOutcomes.Healthy, "CygNet answered a facility query for the owning service.", actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CygNetUnavailableException e) { return new(AdapterTestOutcomes.NeedsAttention, e.Message, actingAs); }
        catch (Exception) { return new(AdapterTestOutcomes.Unreachable, "CygNet could not complete the connection check.", actingAs); }
    }

    public static async Task<DiscoveryResult> DiscoverAsync(ICygNetFacilityReader reader, CygNetConnection connection,
        string actingAs, CancellationToken cancellationToken = default)
    {
        try
        {
            var batch = await reader.ReadFacilitiesAsync(connection, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = MapFacilities(connection, batch.Facilities, actingAs);
            return mapped with { Complete = mapped.Complete && batch.Complete,
                Warnings = mapped.Warnings.Concat(batch.Complete ? Array.Empty<string>() : ["CygNet discovery was incomplete. Review the limit or failed facility reads before importing."]).ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CygNetUnavailableException e) { return new(false, [], [e.Message], actingAs); }
        catch (Exception) { return new(false, [], ["CygNet could not complete facility discovery."], actingAs); }
    }

    public static DiscoveryResult MapFacilities(CygNetConnection connection,
        IReadOnlyCollection<CygNetFacilitySnapshot> facilities, string actingAs)
    {
        var items = new List<DiscoveredAsset>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in facilities.Take(MaximumFacilities))
        {
            if (!connection.Contains(connection.TagFor(f.Id))) continue;
            if (!ids.Add(f.Id)) return new(false, [], ["CygNet returned duplicate facility identities."], actingAs);
            var facts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["domain"] = connection.Domain.ToString(CultureInfo.InvariantCulture), ["site"] = connection.Site,
                ["service"] = connection.Service, ["facilityId"] = f.Id, ["facilityTag"] = connection.TagFor(f.Id),
                ["description"] = f.Description ?? "", ["facilityType"] = f.Type ?? ""
            };
            items.Add(new(connection.TagFor(f.Id), f.Id, ResourceKind, null, facts));
        }
        var complete = items.Count == facilities.Count;
        return new(complete, items.OrderBy(i => i.ExternalId, StringComparer.Ordinal).ToArray(),
            complete ? [] : ["Some facilities could not be represented or the discovery limit was reached."], actingAs,
            UnavailableFields: []);
    }
}

public sealed record CygNetConnection(ushort Domain, string Site, string Service)
{
    public string SiteService => Site + "." + Service;
    public string TagFor(string facilityId) => SiteService + "::" + facilityId;
    public bool Contains(string? tag)
    {
        var prefix = SiteService + "::";
        return tag is not null && tag.Length <= CygNetAdapter.MaximumFacilityTagLength &&
            tag.StartsWith(prefix, StringComparison.Ordinal) && tag.Length > prefix.Length &&
            !tag[prefix.Length..].Contains("::", StringComparison.Ordinal) && !tag.Any(char.IsControl) && tag == tag.Trim();
    }
}
public sealed record CygNetQuantityMapping(string Udc, string Unit);
public sealed record CygNetFacilitySnapshot(string Id, string? Description = null, string? Type = null);
public sealed record CygNetFacilityBatch(IReadOnlyCollection<CygNetFacilitySnapshot> Facilities, bool Complete);
public interface ICygNetFacilityReader
{
    Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken);
    Task<CygNetFacilityBatch> ReadFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken);
}
public sealed class CygNetUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class CygNetUnsupportedFacilityReader : ICygNetFacilityReader
{
    public const string Message = "This Gateway has no CygNet runtime. Use a Windows x64 build with CYGNET_LIBRARY_PATH set.";
    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => Task.FromException(new CygNetUnavailableException(Message));
    public Task<CygNetFacilityBatch> ReadFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<CygNetFacilityBatch>(new CygNetUnavailableException(Message));
}
public sealed class CygNetReadPolicy : IAdapterReadPolicy
{
    public string AdapterKey => CygNetAdapter.Key;
    public string ResourceKind => CygNetAdapter.ResourceKind;
    public bool IsSource(IReadOnlyDictionary<string, string?> settings, string externalId) =>
        CygNetAdapter.ReadConnection(settings, out _)?.Contains(externalId) == true && CygNetAdapter.ReadMappings(settings) is not null;
    public bool CanRead(IReadOnlyDictionary<string, string?> settings, string externalId, string quantity,
        string access, string? capability, string? schemaId) => IsSource(settings, externalId) && access == "Current" &&
        capability is null && schemaId is null && CygNetAdapter.ReadMappings(settings)!.ContainsKey(quantity);
}
