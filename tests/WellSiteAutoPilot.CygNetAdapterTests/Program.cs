using System.Net.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

const string account = @"DOMAIN\wsa-service";
var descriptor = CygNetAdapter.Descriptor;
Check(descriptor.Key == "cygnet" && descriptor.CanDiscover, "descriptor advertises facility discovery");
Check(descriptor.Fields.Select(field => field.Key).SequenceEqual(["domain", "site", "service"]) && descriptor.Fields.All(field => field.Required),
    "a connection is a domain, a site and a service, all required");
Check(descriptor.GroupingFields!.Single().Key == "facilityType", "facility type is the grouping dimension");

var good = new Dictionary<string, string?> { ["domain"] = "22567", ["site"] = "SITE1", ["service"] = "UIS" };
Check(AdapterSettingsValidator.Validate(descriptor, good).Count == 0, "valid settings accepted");
Check(AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?>()).Count == 3, "all three settings are required");
Check(AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?> { ["domain"] = "abc", ["site"] = "S", ["service"] = "U" }).Count == 1,
    "a domain that is not a number is refused");

var connection = CygNetAdapter.ReadConnection(good, out var problem);
Check(connection is not null && problem is null && connection.Domain == 22567 && connection.SiteService == "SITE1.UIS" &&
      connection.TagFor("WELL1") == "SITE1.UIS::WELL1", "settings become a connection and a facility tag");
Check(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = " 5 ", ["site"] = " A ", ["service"] = " B " }, out _) is { Domain: 5, Site: "A", Service: "B" },
    "settings are trimmed");
foreach (var domain in new[] { "70000", "-1", "1.5", "", null })
    Check(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = domain, ["site"] = "A", ["service"] = "B" }, out var domainProblem) is null &&
          domainProblem!.Contains("domain", StringComparison.Ordinal), $"domain '{domain}' is refused");
foreach (var site in new[] { "SITE.1", "SITE::X", "SI TE", "", new string('A', 33) })
    Check(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = "1", ["site"] = site, ["service"] = "UIS" }, out var siteProblem) is null &&
          siteProblem!.Contains("site", StringComparison.Ordinal), $"site '{site}' is refused");

CygNetFacilitySnapshot[] facilities =
[
    new("WELL0002", "Pad 7 well 2", "Well"),
    new("WELL0001", null, "Well"),
    new("TANK01", "Tank", "Tank")
];
var mapped = CygNetAdapter.MapFacilities(connection!, facilities, account);
Check(mapped.Complete && mapped.Items.Count == 3 && mapped.Warnings.Count == 0 && mapped.ActingAs == account, "all facilities discovered");
Check(mapped.Items.Select(item => item.ExternalId).SequenceEqual(["SITE1.UIS::TANK01", "SITE1.UIS::WELL0001", "SITE1.UIS::WELL0002"]),
    "the external id is the full facility tag, in a stable order");
Check(mapped.Items.All(item => item.ProviderType == CygNetAdapter.ResourceKind && item.ParentExternalId is null), "flat facilities of one kind");
Check(mapped.Items[2].DisplayName == "WELL0002" && mapped.Items[2].Metadata["description"] == "Pad 7 well 2" && mapped.Items[2].Metadata["facilityType"] == "Well",
    "the id is the name; description and type are kept as facts");
Check(!mapped.Items[1].Metadata.ContainsKey("description"), "an empty description is not kept");
Check(mapped.Items[0].Metadata["domain"] == "22567" && mapped.Items[0].Metadata["site"] == "SITE1" && mapped.Items[0].Metadata["service"] == "UIS" &&
      mapped.Items[0].Metadata["facilityTag"] == "SITE1.UIS::TANK01", "where it came from is kept as facts");
Check(CygNetAdapter.MapFacilities(connection!, [], account).Complete, "a genuinely empty service is complete");

var partial = CygNetAdapter.MapFacilities(connection!, [new("OK1"), new(" "), new("BAD::ID"), new("LONG" + new string('X', 200))], account);
Check(!partial.Complete && partial.Items.Count == 1 && partial.Warnings.Single().Contains("3 facility", StringComparison.Ordinal),
    "unusable facilities are skipped with a warning and the result is marked incomplete");
var duplicate = CygNetAdapter.MapFacilities(connection!, [new("Well1"), new("WELL1")], account);
Check(!duplicate.Complete && duplicate.Items.Count == 0, "duplicate identities fail closed");

var fetched = await CygNetAdapter.DiscoverAsync(new FixtureReader(facilities), connection!, account);
Check(fetched.Complete && fetched.Items.Count == 3, "the reader seam returns mapped facilities");
var denied = await CygNetAdapter.DiscoverAsync(new FailingReader(new UnauthorizedAccessException("TOKEN-DO-NOT-LEAK")), connection!, account);
Check(!denied.Complete && denied.Items.Count == 0 && !denied.Warnings[0].Contains("TOKEN-DO-NOT-LEAK", StringComparison.Ordinal), "a refused account is explained without vendor text");
var offline = await CygNetAdapter.DiscoverAsync(new FailingReader(new HttpRequestException("No such host https://secret.example/x")), connection!, account);
Check(!offline.Complete && !offline.Warnings[0].Contains("secret.example", StringComparison.Ordinal), "addresses are not echoed");
var unknown = await CygNetAdapter.DiscoverAsync(new FailingReader(new InvalidOperationException("The domain 5 is not known.\r\nTOKENTOKENTOKENTOKENTOKENTOKENTOKENTOKENTOKEN")), connection!, account);
Check(!unknown.Complete && unknown.Warnings.Single().Contains("domain 5 is not known", StringComparison.Ordinal) &&
      !unknown.Warnings.Single().Contains("TOKENTOKEN", StringComparison.Ordinal) && !unknown.Warnings.Single().Contains('\n'),
    "an unexpected failure shows CygNet's reason on one line without token-like words");
var timedOut = await CygNetAdapter.DiscoverAsync(new FailingReader(new OperationCanceledException()), connection!, account);
Check(!timedOut.Complete && timedOut.Warnings.Single().Contains("timed out", StringComparison.Ordinal), "a timeout is a result");
using (var cts = new CancellationTokenSource())
{
    await cts.CancelAsync();
    try
    {
        await CygNetAdapter.DiscoverAsync(new FixtureReader(facilities), connection!, account, cts.Token);
        throw new InvalidOperationException("caller cancellation was swallowed");
    }
    catch (OperationCanceledException) { }
}

var healthy = await CygNetAdapter.CheckAsync(new FixtureReader(facilities), connection!, account);
Check(healthy.Outcome == AdapterTestOutcomes.Healthy && healthy.ActingAs == account && healthy.Message.Contains("SITE1.UIS", StringComparison.Ordinal) &&
      healthy.Message.Contains("22567", StringComparison.Ordinal), "a reachable service tests healthy and names where");
var checkDenied = await CygNetAdapter.CheckAsync(new FailingReader(new UnauthorizedAccessException("TOKEN-DO-NOT-LEAK")), connection!, account);
Check(checkDenied.Outcome == AdapterTestOutcomes.NeedsAttention && !checkDenied.Message.Contains("TOKEN-DO-NOT-LEAK", StringComparison.Ordinal), "a refused account needs attention");
var checkOffline = await CygNetAdapter.CheckAsync(new FailingReader(new HttpRequestException("No such host is known https://secret.example/x")), connection!, account);
Check(checkOffline.Outcome == AdapterTestOutcomes.Unreachable && !checkOffline.Message.Contains("secret.example", StringComparison.Ordinal), "an unreachable server is reported as such");
var checkOther = await CygNetAdapter.CheckAsync(new FailingReader(new InvalidOperationException("Service SITE1.UIS was not found.")), connection!, account);
Check(checkOther.Outcome == AdapterTestOutcomes.NeedsAttention && checkOther.Message.Contains("was not found", StringComparison.Ordinal), "an unexpected failure says why");
var checkTimeout = await CygNetAdapter.CheckAsync(new FailingReader(new OperationCanceledException()), connection!, account);
Check(checkTimeout.Outcome == AdapterTestOutcomes.Unreachable, "a timeout is unreachable");

var unsupported = new CygNetUnsupportedFacilityReader();
var noSupport = await CygNetAdapter.CheckAsync(unsupported, connection!, account);
Check(noSupport.Outcome == AdapterTestOutcomes.NeedsAttention && noSupport.Message.Contains("CYGNET_LIBRARY_PATH", StringComparison.Ordinal),
    "a Gateway built without CygNet says what to do");
var noSupportDiscovery = await CygNetAdapter.DiscoverAsync(unsupported, connection!, account);
Check(!noSupportDiscovery.Complete && noSupportDiscovery.Items.Count == 0 && noSupportDiscovery.Warnings.Single().Contains("CYGNET_LIBRARY_PATH", StringComparison.Ordinal),
    "discovery in such a build is incomplete and says why");

Check(CygNetAdapter.SafeVendorReason(new InvalidOperationException(new string('x', 500) + " " + string.Join(' ', Enumerable.Repeat("word", 100)))).Length <= 203 &&
      CygNetAdapter.SafeVendorReason(new InvalidOperationException(string.Empty)) == "CygNet gave no reason.", "the reason is short and never empty");
Console.WriteLine("CygNet adapter checks passed.");

static void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException("CygNet adapter: " + message);
}

sealed class FixtureReader(IReadOnlyCollection<CygNetFacilitySnapshot> facilities) : ICygNetFacilityReader
{
    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyCollection<CygNetFacilitySnapshot>> ReadFacilitiesAsync(
        CygNetConnection connection, CancellationToken cancellationToken) => Task.FromResult(facilities);
}

sealed class FailingReader(Exception exception) : ICygNetFacilityReader
{
    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => Task.FromException(exception);

    public Task<IReadOnlyCollection<CygNetFacilitySnapshot>> ReadFacilitiesAsync(
        CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyCollection<CygNetFacilitySnapshot>>(exception);
}
