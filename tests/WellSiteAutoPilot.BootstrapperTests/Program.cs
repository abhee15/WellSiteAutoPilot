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

Console.WriteLine("Installer settings checks passed.");
return 0;
