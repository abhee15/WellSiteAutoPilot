using System.Net.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.PetroVisor;

const string account = @"DOMAIN\wsa-service";
var descriptor = PetroVisorAdapter.Descriptor;
Check(descriptor.Key == "petrovisor" && descriptor.CanDiscover, "descriptor advertises discovery");
Check(descriptor.GroupingFields!.Single().Key == "entityType", "entity type is the only grouping dimension");
Check(descriptor.Fields.Count == 3 && descriptor.Fields.Any(x => x.Key == "workspace" && x.Required) &&
    descriptor.Fields.Any(x => x.Key == "baseUrl" && x.Required) &&
    descriptor.Fields.Any(x => x.Key == "identityUrl" && !x.Required), "workspace-bound settings required, identity provider optional");

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

// Keycloak service-account sign-in: where tokens come from, what is asked, and that nothing secret is echoed.
Check(PetroVisorKeycloakSessionFactory.TokenEndpoint(null) is null, "no identity address asks PetroVisor");
Check(PetroVisorKeycloakSessionFactory.TokenEndpoint(new Uri("https://login.example/realms/pv/"))!.AbsoluteUri ==
      "https://login.example/realms/pv/protocol/openid-connect/token", "a realm address gets the token path");
Check(PetroVisorKeycloakSessionFactory.TokenEndpoint(new Uri("https://login.example/custom/token"))!.AbsoluteUri ==
      "https://login.example/custom/token", "a token address is used as given");
Check(PetroVisorAdapter.IdentityAddress(new Dictionary<string, string?>()) is null &&
      PetroVisorAdapter.IdentityAddress(new Dictionary<string, string?> { [PetroVisorAdapter.IdentitySetting] = " " }) is null &&
      PetroVisorAdapter.IdentityAddress(new Dictionary<string, string?> { [PetroVisorAdapter.IdentitySetting] = " https://login.example/realms/pv " })!.Host == "login.example",
    "the identity address is optional and trimmed");
Check(!PetroVisorKeycloakSessionFactory.IsConfigured(_ => null) &&
      PetroVisorKeycloakSessionFactory.IsConfigured(name => name == PetroVisorKeycloakSessionFactory.ClientIdVariable ? "wsa" : "pw"),
    "the service account is configured only with both a client id and a secret");
var noClient = new PetroVisorKeycloakSessionFactory(_ => null);
try { noClient.Create(new Uri("https://petrovisor.example/discovery"), "Production"); throw new InvalidOperationException("created a session without a client"); }
catch (PetroVisorSignInUnavailableException exception)
{
    Check(exception.Message.Contains(PetroVisorKeycloakSessionFactory.ClientSecretVariable, StringComparison.Ordinal), "a missing client names what to set");
}
string? formSeen = null;
using var tokenHandler = new RecordingHandler(async request =>
{
    formSeen = request.Content is null ? null : await request.Content.ReadAsStringAsync();
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"ACCESS-1\",\"expires_in\":300}") };
});
using var keycloak = new PetroVisorKeycloakSessionFactory(name => name == "WSA_PETROVISOR_CLIENT_ID" ? "wsa-gateway" : "s3cret", tokenHandler);
Check(keycloak.Identity == "service account wsa-gateway", "the identity names the service account, never the secret");
var issued = keycloak.RequestAccessToken(new Uri("https://login.example/realms/pv/protocol/openid-connect/token"), "wsa-gateway", "s3cret");
Check(issued == "ACCESS-1" && formSeen is not null && formSeen.Contains("grant_type=client_credentials", StringComparison.Ordinal) &&
      formSeen.Contains("client_id=wsa-gateway", StringComparison.Ordinal), "a service-account token is requested with the client credentials grant");
foreach (var (status, body) in new[] { (System.Net.HttpStatusCode.Unauthorized, "{\"error\":\"SECRET-BODY\"}"),
             (System.Net.HttpStatusCode.OK, "{}"), (System.Net.HttpStatusCode.OK, "not json SECRET-BODY"),
             (System.Net.HttpStatusCode.BadGateway, "SECRET-BODY") })
{
    using var failing = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
    using var failingFactory = new PetroVisorKeycloakSessionFactory(_ => "x", failing);
    try { failingFactory.RequestAccessToken(new Uri("https://login.example/token"), "x", "x"); throw new InvalidOperationException("accepted a bad answer"); }
    catch (PetroVisorSignInUnavailableException exception)
    {
        Check(!exception.Message.Contains("SECRET-BODY", StringComparison.Ordinal), $"a {(int)status} answer is explained without echoing the body");
    }
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

sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(request).GetAwaiter().GetResult();
}
