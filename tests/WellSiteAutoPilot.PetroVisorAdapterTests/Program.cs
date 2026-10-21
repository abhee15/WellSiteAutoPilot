using System.Net.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.PetroVisor;

const string account = @"DOMAIN\wsa-service";
var descriptor = PetroVisorAdapter.Descriptor;
Check(descriptor.Key == "petrovisor" && descriptor.CanDiscover, "descriptor advertises discovery");
Check(descriptor.GroupingFields!.Single().Key == "entityType", "entity type is the only grouping dimension");
Check(descriptor.Fields.Count == 2 && descriptor.Fields.Any(x => x.Key == "workspace" && x.Required) &&
    descriptor.Fields.Any(x => x.Key == "baseUrl" && x.Required), "workspace-bound settings required");

var goodSettings = new Dictionary<string, string?> { ["workspace"] = "Production", ["baseUrl"] = "https://petrovisor.example/api" };
Check(AdapterSettingsValidator.Validate(descriptor, goodSettings).Count == 0, "valid settings accepted");
Check(AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?>()).Count == 2, "both settings required");

var entities = new PetroVisorEntitySnapshot[]
{
    new("ESP-102", "Well", "B"),
    new("ESP-101", "Well"),
    new("Field A", "Field")
};
var mapped = PetroVisorAdapter.MapEntities("Production", entities, account);
Check(mapped.Complete && mapped.Items.Count == 3 && mapped.Warnings.Count == 0, "all entities discovered");
Check(mapped.Items.All(item => item.ParentExternalId is null && item.ProviderType == PetroVisorAdapter.ResourceKind),
    "flat assets only, no source hierarchy");
Check(mapped.Items.Select(item => item.ExternalId).SequenceEqual(["ESP-101", "ESP-102", "Field A"]),
    "stable sorted workspace identities");
Check(mapped.Items[0].Metadata["workspace"] == "Production" && mapped.Items[0].Metadata["entityType"] == "Well",
    "classification and workspace retained as facts");
Check(mapped.Items[1].Metadata["alias"] == "B", "alias retained");
Check(mapped.Items[2].Metadata["entityType"] == "Field", "entity types not hard-coded to wells");
Check(PetroVisorAdapter.MapEntities("Production", [], account).Complete, "genuinely empty workspace is complete");

var dupe = PetroVisorAdapter.MapEntities("Production", [new("Well-A", "Well"), new("well-a", "Well")], account);
Check(!dupe.Complete && dupe.Items.Count == 0, "duplicate identities fail closed");
var unnamed = PetroVisorAdapter.MapEntities("Production", [new(" ", "Well")], account);
Check(!unnamed.Complete && unnamed.Items.Count == 0, "blank identity fails closed");

var fetched = await PetroVisorAdapter.DiscoverAsync(new FixtureReader(entities), "Production", account);
Check(fetched.Complete && fetched.Items.Count == 3, "the read seam returns mapped assets");
var denied = await PetroVisorAdapter.DiscoverAsync(new FailingReader(new UnauthorizedAccessException("TOKEN-DO-NOT-LEAK")), "Production", account);
Check(!denied.Complete && !denied.Warnings[0].Contains("TOKEN-DO-NOT-LEAK", StringComparison.Ordinal),
    "vendor error is redacted");
var offline = await PetroVisorAdapter.DiscoverAsync(new FailingReader(new HttpRequestException("SECRET-URL")), "Production", account);
Check(!offline.Complete && !offline.Warnings[0].Contains("SECRET-URL", StringComparison.Ordinal),
    "transport errors are redacted");
using (var cts = new CancellationTokenSource())
{
    await cts.CancelAsync();
    try
    {
        await PetroVisorAdapter.DiscoverAsync(new FixtureReader(entities), "Production", account, cts.Token);
        throw new InvalidOperationException("caller cancellation was swallowed");
    }
    catch (OperationCanceledException) { }
}

var healthy = await PetroVisorAdapter.CheckAsync(new FixtureReader(entities), "Production", account);
Check(healthy.Outcome == AdapterTestOutcomes.Healthy && healthy.ActingAs == account && healthy.Message.Contains("3 entity types", StringComparison.Ordinal),
    "a readable workspace tests healthy");
var checkDenied = await PetroVisorAdapter.CheckAsync(new FailingReader(new UnauthorizedAccessException("TOKEN-DO-NOT-LEAK")), "Production", account);
Check(checkDenied.Outcome == AdapterTestOutcomes.NeedsAttention && !checkDenied.Message.Contains("TOKEN-DO-NOT-LEAK", StringComparison.Ordinal),
    "a refused account needs attention without leaking vendor text");
var checkOffline = await PetroVisorAdapter.CheckAsync(new FailingReader(new HttpRequestException("SECRET-URL")), "Production", account);
Check(checkOffline.Outcome == AdapterTestOutcomes.Unreachable && !checkOffline.Message.Contains("SECRET-URL", StringComparison.Ordinal),
    "an unreachable server is reported as unreachable");
var noSignIn = await PetroVisorAdapter.CheckAsync(
    new FailingReader(new PetroVisorSignInUnavailableException("The Gateway has no PetroVisor sign-in yet.")), "Production", account);
Check(noSignIn.Outcome == AdapterTestOutcomes.NeedsAttention && noSignIn.Message.Contains("no PetroVisor sign-in", StringComparison.Ordinal),
    "a missing sign-in is explained");
var noEnv = new PetroVisorPasswordSessionFactory(_ => null);
try { noEnv.Create(new Uri("https://petrovisor.example/discovery"), "Production"); throw new InvalidOperationException("created a session without a sign-in"); }
catch (PetroVisorSignInUnavailableException exception)
{
    Check(exception.Message.Contains(PetroVisorPasswordSessionFactory.PasswordVariable, StringComparison.Ordinal) &&
          noEnv.Identity.Contains("no PetroVisor sign-in", StringComparison.Ordinal), "the stopgap sign-in names what is missing");
}

// This inspects installed package types without credentials/network or guessing SSO semantics.
var sdk = PetroVisorSdkProbe.Inspect();
Check(sdk.ProviderConstructorParameterTypes.Count > 0, "SDK exposes provider constructors");
Console.WriteLine($"PetroVisor SDK API assembly version {sdk.ApiAssemblyVersion}; client {sdk.ClientAssemblyVersion}.");
foreach (var signature in sdk.ProviderConstructorParameterTypes)
    Console.WriteLine("PetroVisor SDK provider constructor parameter types: " + signature);
SdkSurface.Emit();
Console.WriteLine("PetroVisor adapter POC checks passed.");

static void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException("PetroVisor POC: " + message);
}

sealed class FixtureReader(IReadOnlyCollection<PetroVisorEntitySnapshot> entities) : IPetroVisorEntityReader
{
    public Task<IReadOnlyCollection<PetroVisorEntitySnapshot>> ReadEntitiesAsync(
        string workspace, CancellationToken cancellationToken) => Task.FromResult(entities);

    public Task<int> CountEntityTypesAsync(string workspace, CancellationToken cancellationToken) => Task.FromResult(3);
}

sealed class FailingReader(Exception exception) : IPetroVisorEntityReader
{
    public Task<IReadOnlyCollection<PetroVisorEntitySnapshot>> ReadEntitiesAsync(
        string workspace, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyCollection<PetroVisorEntitySnapshot>>(exception);

    public Task<int> CountEntityTypesAsync(string workspace, CancellationToken cancellationToken) => Task.FromException<int>(exception);
}
