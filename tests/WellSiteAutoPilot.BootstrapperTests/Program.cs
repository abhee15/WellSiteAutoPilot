using System.Text.Json.Nodes;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Installer settings check failed: " + message + ".");
    }
}

static void Refused(Action action, string expectedPart, string message)
{
    try
    {
        action();
    }
    catch (InvalidOperationException exception)
    {
        Expect(exception.Message.Contains(expectedPart, StringComparison.Ordinal),
            message + " (message was: " + exception.Message + ")");
        return;
    }

    throw new InvalidOperationException("Installer settings check failed: " + message + " was not refused.");
}

static Func<string, string?> Env(params (string Name, string Value)[] values) =>
    name => values.Where(item => item.Name == name).Select(item => item.Value).FirstOrDefault();

// Nothing supplied: no connection requested (an upgrade keeps the stored one).
Expect(InstallerSettings.BuildConnectionString(Env()) is null, "an empty environment must request nothing");
Expect(InstallerSettings.BuildConnectionString(Env(("WSA_DATABASE_PASSWORD", ""), ("WSA_DATABASE_USER", "  "))) is null,
    "blank values must be treated as absent");

// Parts with defaults.
Expect(InstallerSettings.BuildConnectionString(Env(("WSA_DATABASE_USER", "wsa"), ("WSA_DATABASE_PASSWORD", "pw")))
    == "Host=127.0.0.1;Port=5432;Database=wellsite_autopilot;Username=wsa;Password=pw",
    "defaults for host, port and database were not applied");
Expect(InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_HOST", "db.internal"), ("WSA_DATABASE_PORT", "6432"), ("WSA_DATABASE_NAME", "wsa_prod"),
        ("WSA_DATABASE_USER", "wsa_app"), ("WSA_DATABASE_PASSWORD", "pw")))
    == "Host=db.internal;Port=6432;Database=wsa_prod;Username=wsa_app;Password=pw",
    "explicit parts were not used");

// A password with connection-string metacharacters must be quoted, not truncated or injected.
Expect(InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_USER", "wsa"), ("WSA_DATABASE_PASSWORD", "a;Host=evil\"b c")))
    == "Host=127.0.0.1;Port=5432;Database=wellsite_autopilot;Username=wsa;Password=\"a;Host=evil\"\"b c\"",
    "special characters in the password were not quoted");

// Incomplete or invalid input is refused with the variable names.
Refused(() => InstallerSettings.BuildConnectionString(Env(("WSA_DATABASE_HOST", "db"))),
    "WSA_DATABASE_PASSWORD", "a host without credentials");
Refused(() => InstallerSettings.BuildConnectionString(Env(("WSA_DATABASE_USER", "wsa"))),
    "WSA_DATABASE_PASSWORD", "a user without a password");
Refused(() => InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_USER", "wsa"), ("WSA_DATABASE_PASSWORD", "pw"), ("WSA_DATABASE_PORT", "99999"))),
    "WSA_DATABASE_PORT", "an out-of-range port");
Refused(() => InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_USER", "wsa"), ("WSA_DATABASE_PASSWORD", "pw"), ("WSA_DATABASE_PORT", "54x2"))),
    "WSA_DATABASE_PORT", "a non-numeric port");
Refused(() => InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_USER", "wsa"), ("WSA_DATABASE_PASSWORD", "pw\nHost=evil"))),
    "WSA_DATABASE_PASSWORD", "a password with a line break");

// A full connection string wins over parts.
Expect(InstallerSettings.BuildConnectionString(Env(
        ("WSA_DATABASE_CONNECTION_STRING", " Host=x;Database=y;Username=u;Password=p "),
        ("WSA_DATABASE_USER", "ignored"), ("WSA_DATABASE_PASSWORD", "ignored")))
    == "Host=x;Database=y;Username=u;Password=p", "a full connection string must win and be trimmed");

// Settings file: add the connection without losing the security section.
var security = "{\n  \"Security\": {\n    \"BootstrapAdministrators\": [\"CORP\\\\admin\"]\n  }\n}";
Expect(InstallerSettings.ReadConnectionString(security) is null, "a security-only file has no connection");
Expect(InstallerSettings.ReadConnectionString(null) is null && InstallerSettings.ReadConnectionString("  ") is null,
    "a missing file has no connection");

var merged = InstallerSettings.WithConnectionString(security, "Host=a;Password=one");
var mergedRoot = JsonNode.Parse(merged)!.AsObject();
Expect(mergedRoot["Security"]!["BootstrapAdministrators"]![0]!.GetValue<string>() == "CORP\\admin",
    "the bootstrap administrator was lost when the connection was added");
Expect(InstallerSettings.ReadConnectionString(merged) == "Host=a;Password=one", "the stored connection did not round-trip");

// Replacing keeps the rest, including unrelated sections and other connection names.
var withOther = "{\"Other\":{\"Keep\":1},\"ConnectionStrings\":{\"Extra\":\"keep\",\"WellSiteAutoPilot\":\"Host=old\"}}";
var replaced = JsonNode.Parse(InstallerSettings.WithConnectionString(withOther, "Host=new"))!.AsObject();
Expect(replaced["ConnectionStrings"]!["WellSiteAutoPilot"]!.GetValue<string>() == "Host=new" &&
       replaced["ConnectionStrings"]!["Extra"]!.GetValue<string>() == "keep" &&
       replaced["Other"]!["Keep"]!.GetValue<int>() == 1,
    "replacing the connection changed unrelated settings");

// A brand new file.
Expect(InstallerSettings.ReadConnectionString(InstallerSettings.WithConnectionString(null, "Host=z")) == "Host=z",
    "a new settings file did not store the connection");

// Malformed files fail loudly instead of being overwritten.
Refused(() => InstallerSettings.WithConnectionString("[1]", "Host=z"), "JSON object", "a non-object settings file");
Refused(() => InstallerSettings.WithConnectionString("{not json", "Host=z"), "not valid JSON", "invalid JSON");
Refused(() => InstallerSettings.ReadConnectionString("{\"ConnectionStrings\":3}"), "ConnectionStrings",
    "a non-object ConnectionStrings section");
Refused(() => InstallerSettings.ReadConnectionString("{\"ConnectionStrings\":{\"WellSiteAutoPilot\":7}}"), "must be a string",
    "a non-string connection");

// Output printed after a failed migration never carries the secret.
var secretText = InstallerSettings.Redact("failed for Host=a;Password=s3cr3t and user pw s3cr3t", "Host=a;Password=s3cr3t", "s3cr3t", null, "");
Expect(!secretText.Contains("s3cr3t", StringComparison.Ordinal), "a secret survived redaction");

// Network access: parsed strictly, remembered, and merged without disturbing other settings.
Expect(InstallerSettings.ParseNetworkAccess(null) is null && InstallerSettings.ParseNetworkAccess("  ") is null,
    "no option must mean no change");
Expect(InstallerSettings.ParseNetworkAccess("YES") == true && InstallerSettings.ParseNetworkAccess("no") == false,
    "yes and no must parse in any case");
Refused(() => InstallerSettings.ParseNetworkAccess("true"), "yes or no", "an unclear network-access value");
Refused(() => InstallerSettings.ParseNetworkAccess("yes --bootstrap-admin x"), "yes or no", "an option with extra text");

Expect(InstallerSettings.ReadNetworkAccess(security) is null, "a file without the choice remembers nothing");
var remembered = InstallerSettings.WithNetworkAccess(security, enabled: false);
Expect(InstallerSettings.ReadNetworkAccess(remembered) == false, "the network-access choice did not round-trip");
Expect(JsonNode.Parse(remembered)!["Security"]!["BootstrapAdministrators"]![0]!.GetValue<string>() == "CORP\\admin",
    "remembering network access lost the bootstrap administrator");
var both = InstallerSettings.WithNetworkAccess(
    InstallerSettings.WithConnectionString(remembered, "Host=a;Password=one"), enabled: true);
Expect(InstallerSettings.ReadNetworkAccess(both) == true && InstallerSettings.ReadConnectionString(both) == "Host=a;Password=one",
    "the connection and the network choice must coexist");
Refused(() => InstallerSettings.ReadNetworkAccess("{\"Installer\":{\"NetworkAccess\":\"maybe\"}}"), "true or false",
    "a non-boolean remembered choice");

Expect(InstallerSettings.ParseServerPort(null) is null && InstallerSettings.ParseServerPort(" ") is null,
    "no port option means no change");
Expect(InstallerSettings.ParseServerPort("8080") == 8080 && InstallerSettings.ParseServerPort("1") == 1 && InstallerSettings.ParseServerPort("65535") == 65535,
    "valid ports were not accepted");
Refused(() => InstallerSettings.ParseServerPort("0"), "1 to 65535", "port zero");
Refused(() => InstallerSettings.ParseServerPort("65536"), "1 to 65535", "a port above 65535");
Refused(() => InstallerSettings.ParseServerPort("80a"), "1 to 65535", "a port with extra text");
Refused(() => InstallerSettings.ParseServerPort("-5"), "1 to 65535", "a negative port");
Refused(() => InstallerSettings.ParseServerPort("5081"), "another WellSite AutoPilot service", "the Gateway port");
Refused(() => InstallerSettings.ParseServerPort("4222"), "another WellSite AutoPilot service", "the NATS port");
Expect(InstallerSettings.ReadServerPort(security) is null, "a file without a port remembers nothing");
var withPort = InstallerSettings.WithServerPort(remembered, 8443);
Expect(InstallerSettings.ReadServerPort(withPort) == 8443 && InstallerSettings.ReadNetworkAccess(withPort) == false,
    "the port did not round-trip beside the network-access choice");
Expect(InstallerSettings.ReadConnectionString(InstallerSettings.WithServerPort(both, 9000)) == "Host=a;Password=one",
    "remembering the port dropped the stored connection");
Refused(() => InstallerSettings.ReadServerPort("{\"Installer\":{\"ServerPort\":\"abc\"}}"), "port number", "a stored port that is not a number");
Refused(() => InstallerSettings.ReadServerPort("{\"Installer\":{\"ServerPort\":70000}}"), "port number", "a stored port out of range");

const string Thumb = "0123456789ABCDEF0123456789ABCDEF01234567";
const string ThumbLower = "0123456789abcdef0123456789abcdef01234567";
Expect(InstallerSettings.NormalizeThumbprint("\u200e01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67") == Thumb,
    "a pasted thumbprint with spaces and a hidden mark was not normalised");
Refused(() => InstallerSettings.NormalizeThumbprint("1234"), "40 hexadecimal", "a short thumbprint");
Refused(() => InstallerSettings.NormalizeThumbprint(new string('G', 40)), "40 hexadecimal", "a non-hex thumbprint");

Expect(InstallerSettings.ChooseCertificate(null, null, false, null) is { Kind: CertificateKind.SelfSigned },
    "a first install with nothing requested must generate a self-signed certificate");
Expect(InstallerSettings.ChooseCertificate(null, null, false, Thumb) is { Kind: CertificateKind.Stored, Value: Thumb },
    "an upgrade must keep the stored certificate");
Expect(InstallerSettings.ChooseCertificate(ThumbLower, null, false, null) is { Kind: CertificateKind.Store, Value: Thumb },
    "a requested thumbprint must be used");
Expect(InstallerSettings.ChooseCertificate(null, "C:\\certs\\site.pfx", false, Thumb) is { Kind: CertificateKind.Pfx, Value: "C:\\certs\\site.pfx" },
    "a requested PFX file must replace the stored certificate");
Expect(InstallerSettings.ChooseCertificate(null, null, true, Thumb) is { Kind: CertificateKind.SelfSigned },
    "a requested self-signed certificate must replace the stored certificate");
Refused(() => InstallerSettings.ChooseCertificate(Thumb, "x.pfx", false, null), "one certificate source", "two sources");
Refused(() => InstallerSettings.ChooseCertificate(Thumb, null, true, null), "one certificate source", "a thumbprint and self-signed");

Expect(InstallerSettings.ReadCertificate(security) is (null, null), "a file without a certificate remembers nothing");
var withCertificate = InstallerSettings.WithCertificate(both, ThumbLower, "Store");
Expect(InstallerSettings.ReadCertificate(withCertificate) is (Thumb, "Store"), "the certificate did not round-trip");
Expect(InstallerSettings.ReadConnectionString(withCertificate) == "Host=a;Password=one" &&
       InstallerSettings.ReadNetworkAccess(withCertificate) == true,
    "remembering the certificate dropped other settings");
Refused(() => InstallerSettings.ReadCertificate("{\"Tls\":{\"Thumbprint\":5}}"), "must be text", "a stored thumbprint that is not text");

// Internal caller keys: 256-bit random keys, a verifier the Gateway can check, and files that never mix them up.
var firstKey = InstallerSettings.NewServiceKey();
var secondKey = InstallerSettings.NewServiceKey();
Expect(firstKey.Length == 64 && firstKey.All(Uri.IsHexDigit) && firstKey != secondKey, "service keys must be distinct 256-bit hex values");
// The same vector is asserted by the Gateway's own tests, so the two halves cannot drift apart.
Expect(InstallerSettings.ServiceKeyVerifier("abc") == "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=",
    "the verifier must be the base64 SHA-256 of the key");
Expect(InstallerSettings.ReadServiceKey(InstallerSettings.ServiceKeyFileContent(firstKey)) == firstKey,
    "a stored service key did not round-trip");
Expect(InstallerSettings.ReadServiceKey(null) is null && InstallerSettings.ReadServiceKey("{}") is null,
    "a missing key file must read as no key");
var gatewayFile = InstallerSettings.GatewayKeyFileContent(firstKey, secondKey);
Expect(!gatewayFile.Contains(firstKey, StringComparison.Ordinal) && !gatewayFile.Contains(secondKey, StringComparison.Ordinal),
    "the Gateway file must hold verifiers, never keys");
var callers = JsonNode.Parse(gatewayFile)!["Security"]!["Callers"]!;
Expect((string?)callers["server"]!["Verifier"] == InstallerSettings.ServiceKeyVerifier(firstKey) &&
       (string?)callers["worker"]!["Verifier"] == InstallerSettings.ServiceKeyVerifier(secondKey),
    "the Gateway file must verify the Server key as server and the worker key as worker");


// Gateway account: strict names, remembered across upgrades, passwords only where Windows does not hold them.
Expect(InstallerSettings.ParseGatewayAccount(@"CORP\svc-wsa$") == @"CORP\svc-wsa$" && InstallerSettings.ParseGatewayAccount(@".\wsagateway") == @".\wsagateway",
    "domain, managed and local accounts must be accepted");
Expect(InstallerSettings.ParseGatewayAccount(@"nt authority\localservice") == InstallerSettings.DefaultGatewayAccount,
    "the default account must be recognised in any case");
foreach (var bad in new[] { "svc", @"CORP\", @"\svc", @"CORP\svc extra", @"CORP\svc & calc", "svc@corp.example", @"NT AUTHORITY\SYSTEM", @"CORP\a\b" })
{
    Refused(() => InstallerSettings.ParseGatewayAccount(bad), "--gateway-account", "the account name '" + bad + "'");
}

Expect(InstallerSettings.ChooseGatewayAccount(null, false, null) == (InstallerSettings.DefaultGatewayAccount, false),
    "a first install without a choice must stay on LocalService and record nothing");
Expect(InstallerSettings.ChooseGatewayAccount(@"CORP\svc$", false, null) == (@"CORP\svc$", true),
    "a new account must be recorded");
Expect(InstallerSettings.ChooseGatewayAccount(null, false, @"CORP\svc$") == (@"CORP\svc$", false),
    "an upgrade that does not mention the account must keep it");
Expect(InstallerSettings.ChooseGatewayAccount(null, true, @"CORP\svc$") == (InstallerSettings.DefaultGatewayAccount, true),
    "asking for the default must return to LocalService and record the change");
Expect(InstallerSettings.ChooseGatewayAccount(@"CORP\SVC$", false, @"CORP\svc$").Changed == false,
    "the same account in another case is not a change");
Refused(() => InstallerSettings.ChooseGatewayAccount(@"CORP\svc$", true, null), "not both", "a named account together with the default");
Expect(InstallerSettings.GatewayAccountNeedsPassword(@"CORP\svc") && !InstallerSettings.GatewayAccountNeedsPassword(@"CORP\svc$") &&
       !InstallerSettings.GatewayAccountNeedsPassword(InstallerSettings.DefaultGatewayAccount),
    "only an ordinary named account needs a password");
var withAccount = InstallerSettings.WithGatewayAccount(both, @"CORP\svc$");
Expect(InstallerSettings.ReadGatewayAccount(withAccount) == @"CORP\svc$" && InstallerSettings.ReadNetworkAccess(withAccount) == true &&
       InstallerSettings.ReadConnectionString(withAccount) == "Host=a;Password=one",
    "the account must be remembered without disturbing other settings");
Expect(InstallerSettings.ReadGatewayAccount(null) is null && InstallerSettings.ReadGatewayAccount("{}") is null,
    "a file without the account remembers nothing");

// The log-on-as-a-service right is added to an exported policy once, and nothing else is disturbed.
var export = "[Unicode]\r\nUnicode=yes\r\n[Privilege Rights]\r\nSeBackupPrivilege = *S-1-5-32-544\r\nSeServiceLogonRight = *S-1-5-80-1,*S-1-5-32-544\r\n[Version]\r\nsignature=\"$CHICAGO$\"\r\n";
var granted = InstallerSettings.WithServiceLogonRight(export, "*S-1-5-21-9-9-9-1001");
Expect(granted.Changed && granted.Content.Contains("SeServiceLogonRight = *S-1-5-80-1,*S-1-5-32-544,*S-1-5-21-9-9-9-1001", StringComparison.Ordinal) &&
       granted.Content.Contains("SeBackupPrivilege = *S-1-5-32-544", StringComparison.Ordinal),
    "the SID was not appended to the existing service log-on list");
Expect(!InstallerSettings.WithServiceLogonRight(granted.Content, "*S-1-5-21-9-9-9-1001").Changed,
    "granting the same right twice must change nothing");
var missingLine = InstallerSettings.WithServiceLogonRight("[Privilege Rights]\r\nSeBackupPrivilege = *S-1-5-32-544\r\n", "S-1-5-21-5");
Expect(missingLine.Changed && missingLine.Content.Contains("SeServiceLogonRight = *S-1-5-21-5", StringComparison.Ordinal),
    "a policy without the right must gain it");
Expect(InstallerSettings.WithServiceLogonRight("[Unicode]\r\nUnicode=yes\r\n", "*S-1-5-21-5").Content.Contains("[Privilege Rights]", StringComparison.Ordinal),
    "a policy without the section must gain it");

// Services restart themselves when they stop unexpectedly, including after an error exit.
var recovery = InstallerSettings.ServiceRecoveryArguments("WellSiteAutoPilotGateway");
Expect(recovery[0] == "failure" && recovery[1] == "WellSiteAutoPilotGateway" &&
       recovery[^1] == "restart/5000/restart/15000/restart/60000" && recovery.Contains("86400"),
    "a service must be restarted after 5, 15 and 60 seconds, with the count reset after a day");
Expect(string.Join(' ', InstallerSettings.ServiceRecoveryOnErrorExitArguments("X")) == "failureflag X 1",
    "an error exit must count as a failure");

Console.WriteLine("Installer settings checks passed.");
return 0;
