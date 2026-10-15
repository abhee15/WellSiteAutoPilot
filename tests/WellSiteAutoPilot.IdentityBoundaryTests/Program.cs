using System.Security.Claims;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Security;

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

Console.WriteLine("Authenticated identity provider boundary checks passed.");
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
