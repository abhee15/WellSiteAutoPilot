// Plain assertion program, matching the repository's other test executables.
using WellSiteAutoPilot.Dev.Core;
using WellSiteAutoPilot.Server;

var failures = new List<string>();

void Check(string name, bool condition)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

void Throws(string name, Action action, string expectedText)
{
    try
    {
        action();
        failures.Add(name + " (no exception)");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains(expectedText, StringComparison.Ordinal))
    {
    }
    catch (Exception ex)
    {
        failures.Add(name + " (" + ex.GetType().Name + ": " + ex.Message + ")");
    }
}

var target = DatabaseSettings.Parse(
    "Host=localhost;Port=5432;Database=wsa_dev;Username=postgres", null, "secret");

// Parsing
Check("name parsed", target.Name == "wsa_dev");
Check("default template", target.TemplateName == "wsa_dev_template");
Check("maintenance database", target.MaintenanceConnectionString.Contains("Database=postgres", StringComparison.OrdinalIgnoreCase));
Check("target database kept", target.DatabaseConnectionString.Contains("wsa_dev", StringComparison.Ordinal));
Check("password applied", target.DatabaseConnectionString.Contains("secret", StringComparison.Ordinal));
Check("mode defaults to Reuse", DatabaseSettings.ParseMode(null) == DatabaseMode.Reuse);
Check("mode parsed", DatabaseSettings.ParseMode("fromtemplate") == DatabaseMode.FromTemplate);
Throws("bad mode", () => DatabaseSettings.ParseMode("Wipe"), "not valid");
Throws("numeric mode", () => DatabaseSettings.ParseMode("7"), "not valid");
Throws("name without prefix", () => DatabaseSettings.Parse("Host=localhost;Database=production", null, null), "must be");
Throws("name with quote", () => DatabaseSettings.Parse("Host=localhost;Database=wsa_x\";drop", null, null), "must be");
Throws("bad template", () => DatabaseSettings.Parse("Host=localhost;Database=wsa_dev", "Bad Name", null), "must be");
Throws("missing database", () => DatabaseSettings.Parse("Host=localhost", null, null), "Database name");
var synonym = DatabaseSettings.Parse("Server=localhost;Initial Catalog=wsa_dev", null, null);
Check("synonym keys", synonym.Name == "wsa_dev" && synonym.Host == "localhost");

// Planning
Check("reuse existing is a no-op", DatabasePlanner.Plan(DatabaseMode.Reuse, target, true, false).Count == 0);
var create = DatabasePlanner.Plan(DatabaseMode.Reuse, target, false, false);
Check("reuse missing creates", create.Count == 1 && create[0] == "CREATE DATABASE \"wsa_dev\"");
var reset = DatabasePlanner.Plan(DatabaseMode.Reset, target, true, false);
Check("reset drops then creates", reset.Count == 2 && reset[0].StartsWith("DROP DATABASE IF EXISTS", StringComparison.Ordinal)
    && reset[1] == "CREATE DATABASE \"wsa_dev\"");
var fromTemplate = DatabasePlanner.Plan(DatabaseMode.FromTemplate, target, true, true);
Check("from template", fromTemplate.Count == 2 && fromTemplate[1] == "CREATE DATABASE \"wsa_dev\" TEMPLATE \"wsa_dev_template\"");
Throws("from template needs template", () => DatabasePlanner.Plan(DatabaseMode.FromTemplate, target, true, false), "SaveTemplate");
var save = DatabasePlanner.Plan(DatabaseMode.SaveTemplate, target, true, true);
Check("save template", save.Count == 2 && save[1] == "CREATE DATABASE \"wsa_dev_template\" TEMPLATE \"wsa_dev\"");
Throws("save needs database", () => DatabasePlanner.Plan(DatabaseMode.SaveTemplate, target, false, false), "nothing to save");

// Safety: destructive modes refuse a remote server; Reuse is not destructive.
var remote = DatabaseSettings.Parse("Host=db.example.com;Database=wsa_dev", null, null);
Throws("reset remote", () => DatabasePlanner.Plan(DatabaseMode.Reset, remote, true, false), "local server");
Throws("from template remote", () => DatabasePlanner.Plan(DatabaseMode.FromTemplate, remote, true, true), "local server");
Throws("save template remote", () => DatabasePlanner.Plan(DatabaseMode.SaveTemplate, remote, true, true), "local server");
Check("reuse remote allowed", DatabasePlanner.Plan(DatabaseMode.Reuse, remote, true, false).Count == 0);
var loopback = DatabaseSettings.Parse("Host=127.0.0.1;Database=wsa_dev", null, null);
Check("loopback reset allowed", DatabasePlanner.Plan(DatabaseMode.Reset, loopback, true, false).Count == 2);

// Ports
var conflicts = PortCheck.FindConflicts(
    [new PortRequirement("server", 5080), new PortRequirement("NATS", 4222)], port => port == 5080);
Check("one port conflict", conflicts.Count == 1 && conflicts[0].Contains("5080", StringComparison.Ordinal));
Check("no port conflict", PortCheck.FindConflicts([new PortRequirement("server", 5080)], _ => false).Count == 0);

// Frontend static root (Development only)
var defaultRoot = Path.Combine(Path.GetTempPath(), "wsa-default-root");
var configured = Path.Combine(Path.GetTempPath(), "wsa-dev-root-" + Guid.NewGuid().ToString("N"));
try
{
    Check("no setting keeps default", FrontendStaticRoot.Resolve("Development", null, defaultRoot) == defaultRoot);
    Check("blank setting keeps default", FrontendStaticRoot.Resolve("Development", "  ", defaultRoot) == defaultRoot);
    Check("production ignores setting", FrontendStaticRoot.Resolve("Production", configured, defaultRoot) == defaultRoot);
    Check("no environment ignores setting", FrontendStaticRoot.Resolve(null, configured, defaultRoot) == defaultRoot);
    Check("production did not create the folder", !Directory.Exists(configured));
    Check("development uses setting", FrontendStaticRoot.Resolve("Development", configured, defaultRoot) == Path.GetFullPath(configured));
    Check("development creates the folder", Directory.Exists(configured));
}
finally
{
    if (Directory.Exists(configured))
    {
        Directory.Delete(configured, recursive: true);
    }
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("FAILED: " + failure);
    }

    return 1;
}

Console.WriteLine("Dev tooling checks passed.");
return 0;
