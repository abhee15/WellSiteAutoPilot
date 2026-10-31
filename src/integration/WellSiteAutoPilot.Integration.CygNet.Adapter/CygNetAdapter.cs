using System.Globalization;
using System.Net.Http;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>
/// What AutoPilot knows about a CygNet connection: a domain, the site.service of one facility service and an optional
/// facility filter. Read-only: it never writes to CygNet. The CygNet API itself sits behind <see cref="ICygNetClient"/>.
/// </summary>
public static class CygNetAdapter
{
    public const string Key = AdapterReadRules.CygNetKey;
    public const string DomainSetting = "domain";
    public const string SiteServiceSetting = "siteService";
    public const string FacilityFilterSetting = "facilityFilter";
    public const string PointMapSetting = "pointMap";
    public const string ResourceKind = AdapterReadRules.CygNetResourceKind;

    /// <summary>Longest facility tag a read accepts; a longer discovered facility is not offered.</summary>
    public const int MaximumFacilityTagLength = 128;

    public static AdapterDescriptor Descriptor { get; } = new(
        Key, "CygNet",
        "Facilities held in one CygNet facility service. The Integration Gateway reads CygNet as the Windows account it runs as, " +
        "so that account needs a CygNet licence and access to the domain.",
        1,
        [
            new AdapterSettingField(DomainSetting, "Domain",
                "The CygNet domain number, for example 22567.", AdapterSettingKind.WholeNumber, true, 5),
            new AdapterSettingField(SiteServiceSetting, "Site and service",
                "The facility service as SITE.SERVICE, for example WSANET.UIS.", AdapterSettingKind.Text, true, 65),
            new AdapterSettingField(FacilityFilterSetting, "Facility filter",
                "Optional. Only facilities whose id matches are listed. Use * for any characters and ? for one, for example WELL_*.",
                AdapterSettingKind.Text, false, 64),
            new AdapterSettingField(PointMapSetting, "Point mapping",
                "Optional. Which CygNet point serves a quantity, as Quantity=UDC|unit separated by semicolons, for example " +
                "MotorAmps=MTRAMPS|A;PumpFillage=RRLPFILCUR|%. Quantities not listed use the Gateway's defaults.",
                AdapterSettingKind.Text, false, 400)
        ],
        [
            new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Facilities"),
            new ProviderCapability(ProviderCapabilityKind.CurrentData, "Current point values")
        ],
        [
            new DiscoveryField("category", "Category", "Uncategorised"),
            new DiscoveryField("facilityType", "Facility type", "Unspecified type")
        ]);

    /// <summary>Reads and checks the settings. Returns null with a plain-words reason when they cannot be used.</summary>
    public static CygNetConnection? ReadConnection(IReadOnlyDictionary<string, string?> settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        problem = null;
        settings.TryGetValue(DomainSetting, out var rawDomain);
        settings.TryGetValue(SiteServiceSetting, out var rawSiteService);
        settings.TryGetValue(FacilityFilterSetting, out var rawFilter);
        settings.TryGetValue(PointMapSetting, out var rawPointMap);
        if (!int.TryParse(rawDomain?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var domain) || domain is < 0 or > 65535)
        {
            problem = "The domain must be a whole number from 0 to 65535.";
            return null;
        }
        var parts = (rawSiteService ?? string.Empty).Trim().Split('.');
        if (parts.Length != 2 || !IsName(parts[0]) || !IsName(parts[1]))
        {
            problem = "The site and service must be written SITE.SERVICE, using only letters, digits, underscores and hyphens.";
            return null;
        }
        var filter = rawFilter?.Trim();
        if (!string.IsNullOrEmpty(filter) && !filter.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '*' or '?'))
        {
            problem = "The facility filter may use only letters, digits, underscores, hyphens, dots, * and ?.";
            return null;
        }
        var pointMap = ParsePointMap(rawPointMap, out problem);
        if (problem is not null) return null;
        return new CygNetConnection(domain, parts[0], parts[1], string.IsNullOrEmpty(filter) ? null : filter, pointMap);
    }

    private static Dictionary<string, CygNetPointMapping>? ParsePointMap(string? raw, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var map = new Dictionary<string, CygNetPointMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2);
            var quantity = pair[0].Trim();
            var parts = (pair.Length == 2 ? pair[1] : string.Empty).Split('|');
            var canonical = AdapterReadRules.CygNetQuantities.FirstOrDefault(
                item => string.Equals(item, quantity, StringComparison.OrdinalIgnoreCase));
            if (canonical is null || parts.Length != 2 || !IsName(parts[0].Trim()) || parts[1].Trim() is not { Length: > 0 and <= 12 } unit ||
                unit.Any(char.IsControl) || !map.TryAdd(canonical, new CygNetPointMapping(parts[0].Trim(), unit)))
            {
                problem = "The point mapping must be Quantity=UDC|unit pairs separated by semicolons, each quantity once, " +
                    "using a quantity CygNet offers (" + string.Join(", ", AdapterReadRules.CygNetQuantities.Order()) + ").";
                return null;
            }
        }
        return map;
    }

    private static bool IsName(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    /// <summary>Tries the connection: reaches the facility service. Failures are results, never exceptions.</summary>
    public static async Task<AdapterTestResult> CheckAsync(
        ICygNetClient client, CygNetConnection connection, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);
        try
        {
            await client.CheckAsync(connection, cancellationToken);
            return new(AdapterTestOutcomes.Healthy,
                $"CygNet answered and this account can reach {connection.SiteService} in domain {connection.Domain}.", actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(AdapterTestOutcomes.Unreachable, "CygNet did not answer in time.", actingAs);
        }
        catch (CygNetUnavailableException exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention, exception.Message, actingAs);
        }
        catch (UnauthorizedAccessException)
        {
            return new(AdapterTestOutcomes.NeedsAttention, AccountRefused, actingAs);
        }
        catch (HttpRequestException)
        {
            return new(AdapterTestOutcomes.Unreachable, "CygNet could not be reached.", actingAs);
        }
        catch (Exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "The CygNet check failed. See the Gateway log for what CygNet said.", actingAs);
        }
    }

    private const string AccountRefused = "CygNet did not accept this account. Check its CygNet licence and access to the domain.";

    /// <summary>
    /// Lists the facilities of the connection's service. A failure is a result, not an exception: an incomplete answer with
    /// the reason, so nothing downstream treats it as "there are no facilities".
    /// </summary>
    public static async Task<DiscoveryResult> DiscoverAsync(
        ICygNetClient client, CygNetConnection connection, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);
        try
        {
            var list = await client.ListFacilitiesAsync(connection, cancellationToken);
            return MapFacilities(connection, list, actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Incomplete("CygNet did not answer before discovery timed out.", actingAs);
        }
        catch (CygNetUnavailableException exception)
        {
            return Incomplete(exception.Message, actingAs);
        }
        catch (UnauthorizedAccessException)
        {
            return Incomplete(AccountRefused, actingAs);
        }
        catch (HttpRequestException)
        {
            return Incomplete("CygNet could not be reached.", actingAs);
        }
        catch (Exception)
        {
            return Incomplete("CygNet facility discovery failed. See the Gateway log for what CygNet said.", actingAs);
        }
    }

    /// <summary>
    /// Turns what the client found into discovered assets. The external id is the full facility tag (SITE.SERVICE::ID),
    /// which is what a read addresses. The facility filter is applied here. A facility that cannot be used is skipped with
    /// a warning and the result is marked incomplete; duplicate identities stop everything.
    /// </summary>
    public static DiscoveryResult MapFacilities(CygNetConnection connection, CygNetFacilityList list, string actingAs)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);

        var warnings = new List<string>();
        var usable = new List<CygNetFacility>();
        var skipped = 0;
        foreach (var facility in list.Facilities)
        {
            var id = facility.Id?.Trim();
            if (string.IsNullOrEmpty(id) || id.Any(char.IsControl) || id.Contains("::", StringComparison.Ordinal) ||
                connection.TagFor(id).Length > MaximumFacilityTagLength)
            {
                skipped++;
                continue;
            }
            if (connection.FacilityFilter is { } filter && !Matches(filter, id)) continue;
            usable.Add(facility with { Id = id });
        }
        if (skipped > 0) warnings.Add($"{skipped} facility(ies) had no usable id and were skipped.");
        if (list.Truncated)
            warnings.Add("CygNet has more facilities than were read. Narrow the facility filter to list the rest.");

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (usable.Any(facility => !identities.Add(facility.Id)))
            return Incomplete("CygNet returned duplicate facility identities. Nothing was imported.", actingAs);

        var mapped = usable
            .OrderBy(facility => facility.Id, StringComparer.OrdinalIgnoreCase)
            .Select(facility =>
            {
                var tag = connection.TagFor(facility.Id);
                var facts = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["domain"] = connection.Domain.ToString(CultureInfo.InvariantCulture),
                    ["site"] = connection.Site,
                    ["service"] = connection.Service,
                    ["facilityId"] = facility.Id,
                    ["facilityTag"] = tag
                };
                if (!string.IsNullOrWhiteSpace(facility.Description)) facts["description"] = facility.Description.Trim();
                if (!string.IsNullOrWhiteSpace(facility.Type)) facts["facilityType"] = facility.Type.Trim();
                if (!string.IsNullOrWhiteSpace(facility.Category)) facts["category"] = facility.Category.Trim();
                return new DiscoveredAsset(tag, facility.Id, ResourceKind, null, facts);
            }).ToArray();

        return new DiscoveryResult(skipped == 0 && !list.Truncated, mapped, warnings, actingAs);
    }

    /// <summary>Case-insensitive wildcard match: * any characters, ? one character.</summary>
    public static bool Matches(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);
        int p = 0, v = 0, star = -1, mark = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' ||
                char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v]) && pattern[p] != '*')) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = v; }
            else if (star >= 0) { p = star + 1; v = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>
    /// Reads one current value for a saved connection. The external id must be a facility tag of this connection's
    /// service. Failures carry a stable code and a fixed sentence; what CygNet said stays in the inner exception.
    /// </summary>
    public static async Task<EngineeringValue> ReadCurrentAsync(
        ICygNetClient client, CygNetConnection connection, string externalId, string quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(connection);
        if (!AdapterReadRules.CygNetQuantities.Contains(quantity ?? string.Empty))
            throw Failure("CYGNET_QUANTITY_UNSUPPORTED", FailureKind.Validation, "This quantity is not available from CygNet.");
        var prefix = connection.SiteService + "::";
        if (externalId is null || externalId.Length > MaximumFacilityTagLength || externalId.Any(char.IsControl) ||
            !externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || externalId.Length == prefix.Length ||
            externalId.IndexOf("::", prefix.Length, StringComparison.Ordinal) >= 0)
            throw Failure("CYGNET_FACILITY_INVALID", FailureKind.Validation,
                "The asset external id is not a facility tag of this CygNet connection (SITE.SERVICE::FACILITYID).");
        try
        {
            var value = await client.ReadCurrentAsync(connection, externalId[prefix.Length..], quantity!, cancellationToken);
            if (value.Value is null || value.TimestampUtc == default)
                throw Failure("CYGNET_VALUE_INVALID", FailureKind.DependencyUnavailable, "CygNet returned a value that cannot be used.");
            return value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("CYGNET_READ_TIMEOUT", FailureKind.DependencyTimeout, "CygNet did not return the value in time.");
        }
        catch (WellSiteAutoPilotException)
        {
            throw;
        }
        catch (CygNetReadException error)
        {
            throw Failure(error.Code, error.Retryable ? FailureKind.DependencyUnavailable : FailureKind.Validation, error.Message, error);
        }
        catch (CygNetUnavailableException error)
        {
            throw Failure("CYGNET_UNAVAILABLE", FailureKind.DependencyUnavailable, error.Message, error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw Failure("CYGNET_READ_FAILED", FailureKind.DependencyUnavailable, "CygNet could not provide the value.", error);
        }
    }

    private static DiscoveryResult Incomplete(string reason, string actingAs) => new(false, [], [reason], actingAs);

    private static WellSiteAutoPilotException Failure(string code, FailureKind kind, string detail, Exception? inner = null) =>
        new(code, kind, detail,
            retryable: kind is FailureKind.DependencyUnavailable or FailureKind.DependencyTimeout, innerException: inner);
}
