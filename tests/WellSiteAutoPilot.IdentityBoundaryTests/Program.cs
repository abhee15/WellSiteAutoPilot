using System.Security.Claims;
using System.Security.Principal;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Security;

WindowsAuthenticationDiagnosticChecks.Run();
WindowsGroupResolverChecks.Run();
SecurityActorFactoryChecks.Run();
RequestAccessContextChecks.Run();
await GrantPermissionChecks.RunAsync();
await GrantIdentityMiddlewareChecks.RunAsync();

var windows = new ConfiguredPrincipalResolver(
    SecurityOptions.NegotiateAuthenticationMode);
var testHeader = new ConfiguredPrincipalResolver(
    SecurityOptions.TestHeaderAuthenticationMode);

var windowsPrincipal = Principal("CORP\\Engineer",
    new Claim(ClaimTypes.PrimarySid, "S-1-5-21-12345-67890-13579"),
    new Claim("name", "Engineering Operator"),
    new Claim("wsa:test-display-name", "Untrusted CI Alias"));
var windowsIdentity = windows.Resolve(windowsPrincipal);
if (windowsIdentity.ProviderId != "windows" ||
    windowsIdentity.SubjectId != "S-1-5-21-12345-67890-13579" ||
    windowsIdentity.PersistedIdentityKey != "SID:S-1-5-21-12345-67890-13579" ||
    windowsIdentity.IdentityName != "CORP\\Engineer" ||
    windowsIdentity.DisplayName != "Engineering Operator")
{
    throw new InvalidOperationException(
        "Windows SID and display name did not preserve existing persisted identity semantics.");
}

if (new ConfiguredPrincipalResolver("nEgOtIaTe")
        .Resolve(windowsPrincipal).PersistedIdentityKey !=
    windowsIdentity.PersistedIdentityKey)
{
    throw new InvalidOperationException(
        "Authentication mode capitalization changed Windows account identity.");
}

var nameOnlyPrincipal = Principal("  corp\\Operator  ");
var nameOnlyIdentity = windows.Resolve(nameOnlyPrincipal);
var variantNameOnly = windows.Resolve(Principal("CORP\\operator"));
if (nameOnlyIdentity.ProviderId != "windows" ||
    nameOnlyIdentity.PersistedIdentityKey != "NAME:CORP\\OPERATOR" ||
    variantNameOnly.PersistedIdentityKey != nameOnlyIdentity.PersistedIdentityKey ||
    nameOnlyIdentity.SubjectId != "CORP\\OPERATOR")
{
    throw new InvalidOperationException(
        "Fallback Windows account key changed or was not normalized consistently.");
}

var ciPrincipal = Principal("CI\\wsa-admin",
    new Claim("wsa:test-display-name", "CI Administrator"));
var ciIdentity = testHeader.Resolve(ciPrincipal);
if (ciIdentity.ProviderId != "ci-test-header" ||
    ciIdentity.PersistedIdentityKey != "NAME:CI\\WSA-ADMIN" ||
    ciIdentity.DisplayName != "CI Administrator" ||
    windows.Resolve(ciPrincipal).DisplayName is not null)
{
    throw new InvalidOperationException(
        "Test-only display claims leaked into Windows identity resolution.");
}

// Only the authenticated primary identity may supply the SID. Appending
// another identity with misleading provider-specific claims cannot rebind
// the WSA account or inherit the other subject's grants.
var noSidPrimary = Principal("CORP\\Operator");
noSidPrimary.AddIdentity(new ClaimsIdentity(
    [new Claim(ClaimTypes.PrimarySid, "S-1-5-21-99999")], "untrusted-secondary"));
if (windows.Resolve(noSidPrimary).PersistedIdentityKey !=
    "NAME:CORP\\OPERATOR")
{
    throw new InvalidOperationException(
        "An untrusted secondary identity changed the authenticated user key.");
}

ExpectCode(
    () => windows.Resolve(new ClaimsPrincipal(new ClaimsIdentity())),
    "SECURITY_IDENTITY_REQUIRED");
ExpectCode(
    () => windows.Resolve(PrincipalWithoutName()),
    "SECURITY_IDENTITY_REQUIRED");
ExpectCode(
    () => windows.Resolve(Principal("CORP\\Bad\nName")),
    "SECURITY_IDENTITY_INVALID");
ExpectCode(
    () => windows.Resolve(Principal("CORP\\Operator",
        new Claim(ClaimTypes.PrimarySid, "  "))),
    "SECURITY_IDENTITY_KEY_INVALID");
ExpectCode(
    () => windows.Resolve(Principal("CORP\\Operator",
        new Claim(ClaimTypes.PrimarySid, "S-1-5-21-111"),
        new Claim(ClaimTypes.PrimarySid, "S-1-5-21-222"))),
    "SECURITY_IDENTITY_SID_AMBIGUOUS");
ExpectCode(
    () => windows.Resolve(Principal("CORP\\Operator",
        new Claim(ClaimTypes.PrimarySid, "S-1-5-21\n333"))),
    "SECURITY_IDENTITY_KEY_INVALID");
ExpectCode(
    () => new ConfiguredPrincipalResolver("oidc").Resolve(nameOnlyPrincipal),
    "SECURITY_IDENTITY_PROVIDER_UNSUPPORTED");

foreach (var sid in new[]
{
    "not-a-sid", "S-2-5-21", "S-1-5", "S-1-5-", "S-1-5--1",
    "S-1-5-+1", "S-1-05-1", "S-1-5-01", "S-1-5-4294967296",
    "S-1-4294967296-1", "S-1-0x000000000005-1", "S-1-0x1000000000000-1",
    "S-1-5-１２", "S-1-5-1 2", "S-1-5" + string.Concat(Enumerable.Repeat("-1", 16))
})
    ExpectCode(() => windows.Resolve(Principal("CORP\\Operator", new Claim(ClaimTypes.PrimarySid, sid))),
        "SECURITY_IDENTITY_KEY_INVALID");
foreach (var sid in new[]
{
    "S-1-0-0", "S-1-5-4294967295", "S-1-4294967295-1",
    "S-1-0x000100000000-1", "S-1-0xffffffffffff-4294967295",
    "S-1-5" + string.Concat(Enumerable.Repeat("-4294967295", 15))
})
{
    if (windows.Resolve(Principal("CORP\\Operator", new Claim(ClaimTypes.PrimarySid, sid)))
        .PersistedIdentityKey != "SID:" + sid.ToUpperInvariant())
        throw new InvalidOperationException("Valid SID boundary changed its persisted account key.");
    if (OperatingSystem.IsWindows())
    {
        var native = new SecurityIdentifier(sid);
        if (native.BinaryLength != 8 + 4 * (sid.Split('-').Length - 3) ||
            !native.Equals(new SecurityIdentifier(native.Value)))
            throw new InvalidOperationException("SID boundary disagrees with the native Windows representation.");
    }
}
ExpectCode(() => windows.Resolve(Principal("CORP\\Operator", new Claim(ClaimTypes.Name, "CORP\\Other"))),
    "SECURITY_IDENTITY_AMBIGUOUS");

Console.WriteLine("Authenticated identity provider boundary checks passed.");
if (args.Length == 1 && args[0] == "--persistence")
    await PersistedIdentityChecks.RunAsync();
else if (args.Length != 0)
    throw new InvalidOperationException("Unsupported identity test arguments.");
return 0;

static ClaimsPrincipal Principal(string accountName, params Claim[] additional)
{
    var claims = new List<Claim> { new(ClaimTypes.Name, accountName) };
    claims.AddRange(additional);
    return new ClaimsPrincipal(new ClaimsIdentity(
        claims, "HostAuthenticated", ClaimTypes.Name, ClaimTypes.Role));
}

static ClaimsPrincipal PrincipalWithoutName() =>
    new(new ClaimsIdentity(
        [new Claim(ClaimTypes.PrimarySid, "S-1-5-21-123")],
        "HostAuthenticated"));

static void ExpectCode(Action assertion, string expectedCode)
{
    try
    {
        assertion();
    }
    catch (WellSiteAutoPilotException error)
        when (error.Code == expectedCode &&
              error.Kind == FailureKind.Authorization)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Identity boundary did not deny malformed input with {expectedCode}.");
}
