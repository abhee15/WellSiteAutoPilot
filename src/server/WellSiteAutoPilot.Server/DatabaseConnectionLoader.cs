using System.Text.Json;

namespace WellSiteAutoPilot.Server;

/// <summary>
/// Chooses the PostgreSQL connection string. Precedence: ConnectionStrings:WellSiteAutoPilot
/// (including its environment form), WSA_DATABASE_CONNECTION_STRING, then the protected machine
/// settings file written by the installer. A passwordless development default is used only when the
/// Server is not running as an installed Windows service; an installed service with no configured
/// connection refuses to start with a clear message instead of failing later with a cryptic
/// "no password has been provided" on the first request.
/// </summary>
public static class DatabaseConnectionLoader
{
    public const string SectionName = "ConnectionStrings";
    public const string ConnectionName = "WellSiteAutoPilot";
    public const string EnvironmentVariable = "WSA_DATABASE_CONNECTION_STRING";
    public const string DevelopmentDefault =
        "Host=127.0.0.1;Port=5432;Database=wellsite_autopilot;Username=wsa";

    public static string Resolve(
        IConfiguration configuration,
        Func<string, string?> environment,
        string settingsPath,
        bool runningAsService)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        var configured = Clean(configuration.GetConnectionString(ConnectionName)) ??
                         Clean(environment(EnvironmentVariable)) ??
                         ReadSettingsFile(settingsPath);
        if (configured is not null)
        {
            return configured;
        }

        if (runningAsService)
        {
            throw new InvalidOperationException(
                "No PostgreSQL connection is configured for the WellSite AutoPilot Server. " +
                $"Expected {SectionName}:{ConnectionName} in '{settingsPath}' (written by the installer) " +
                $"or the {EnvironmentVariable} environment variable. Re-run the installer and supply the " +
                "database connection.");
        }

        return DevelopmentDefault;
    }

    public static string? ReadSettingsFile(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(SectionName, out var section))
        {
            return null;
        }

        if (section.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Settings file '{settingsPath}' must contain {SectionName} as an object.");
        }

        if (!section.TryGetProperty(ConnectionName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Settings file '{settingsPath}' must contain {SectionName}:{ConnectionName} as a string.");
        }

        return Clean(value.GetString());
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
