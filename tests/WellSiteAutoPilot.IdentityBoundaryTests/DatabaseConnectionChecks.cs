using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.Server;

/// <summary>
/// The installed Server must never silently fall back to a passwordless database connection.
/// </summary>
internal static class DatabaseConnectionChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wsa-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            RunIn(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("Database connection precedence and installed-service refusal checks passed.");
    }

    private static void RunIn(string directory)
    {
        var file = Path.Combine(directory, "server.settings.json");
        var none = new ConfigurationBuilder().Build();
        var withSetting = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:WellSiteAutoPilot"] = "Host=config" }).Build();
        Func<string, string?> noEnvironment = _ => null;
        Func<string, string?> environment = name =>
            name == DatabaseConnectionLoader.EnvironmentVariable ? "Host=env" : null;

        // No file at all.
        Expect(DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: false)
            == DatabaseConnectionLoader.DevelopmentDefault, "development default was not used outside a service");
        var refusal = Throws(() => DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: true));
        Expect(refusal.Contains(file, StringComparison.Ordinal) &&
               refusal.Contains("installer", StringComparison.OrdinalIgnoreCase),
            "an installed service with no connection must refuse to start and say where to configure it");

        // The security-only file written by older installers is the same as no connection.
        File.WriteAllText(file, "{\"Security\":{\"BootstrapAdministrators\":[\"CI\\\\admin\"]}}");
        Throws(() => DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: true));

        // Settings file supplies the connection.
        File.WriteAllText(file,
            "{\"Security\":{},\"ConnectionStrings\":{\"WellSiteAutoPilot\":\"  Host=file;Password=secret  \"}}");
        Expect(DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: true)
            == "Host=file;Password=secret", "settings file connection was not used or not trimmed");

        // Precedence: configuration, then environment, then file.
        Expect(DatabaseConnectionLoader.Resolve(withSetting, environment, file, true) == "Host=config",
            "configuration must win over environment and file");
        Expect(DatabaseConnectionLoader.Resolve(none, environment, file, true) == "Host=env",
            "environment must win over the settings file");

        // A blank value is treated as not configured, never as a connection.
        File.WriteAllText(file, "{\"ConnectionStrings\":{\"WellSiteAutoPilot\":\"   \"}}");
        Throws(() => DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: true));

        // Malformed shapes fail loudly rather than being ignored.
        File.WriteAllText(file, "{\"ConnectionStrings\":[]}");
        Throws(() => DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: false));
        File.WriteAllText(file, "{\"ConnectionStrings\":{\"WellSiteAutoPilot\":5}}");
        Throws(() => DatabaseConnectionLoader.Resolve(none, noEnvironment, file, runningAsService: false));
    }

    private static string Throws(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("Expected the database connection resolution to be refused.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Database connection check failed: " + message + ".");
        }
    }
}
