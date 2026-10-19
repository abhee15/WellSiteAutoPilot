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

Console.WriteLine("Installer settings checks passed.");
return 0;
