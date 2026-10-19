using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Platform-neutral logic for the machine settings file (server.settings.json) and the database
/// connection the installer configures. Kept free of Windows APIs so it can be tested on any OS.
/// </summary>
internal static class InstallerSettings
{
    public const string ConnectionStringsSection = "ConnectionStrings";
    public const string ConnectionStringName = "WellSiteAutoPilot";

    public const string FullConnectionStringVariable = "WSA_DATABASE_CONNECTION_STRING";
    public const string HostVariable = "WSA_DATABASE_HOST";
    public const string PortVariable = "WSA_DATABASE_PORT";
    public const string NameVariable = "WSA_DATABASE_NAME";
    public const string UserVariable = "WSA_DATABASE_USER";
    public const string PasswordVariable = "WSA_DATABASE_PASSWORD";

    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 5432;
    private const string DefaultDatabase = "wellsite_autopilot";

    public const string InstallerSection = "Installer";
    public const string NetworkAccessName = "NetworkAccess";
    public const string ServerPortName = "ServerPort";
    public const int DefaultServerPort = 5080;

    // The loopback ports of the services that run beside the Server.
    private static readonly int[] ReservedPorts = [5081, 5082, 4222];

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Builds the database connection string the operator supplied, or null when none was supplied.
    /// A full connection string wins; otherwise the parts are combined and must include both a user
    /// name and a password, because the installed Server never falls back to a passwordless default.
    /// The password reaches the bootstrapper through the environment, never the command line.
    /// </summary>
    public static string? BuildConnectionString(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var full = Clean(environment(FullConnectionStringVariable));
        if (full is not null)
        {
            RejectControlCharacters(full, FullConnectionStringVariable);
            return full;
        }

        var host = Clean(environment(HostVariable));
        var port = Clean(environment(PortVariable));
        var database = Clean(environment(NameVariable));
        var user = Clean(environment(UserVariable));
        var password = environment(PasswordVariable);
        if (string.IsNullOrEmpty(password)) password = null;

        if (host is null && port is null && database is null && user is null && password is null)
        {
            return null;
        }

        if (user is null || password is null)
        {
            throw new InvalidOperationException(
                "Database configuration needs both a user name and a password " +
                $"({UserVariable} and {PasswordVariable}).");
        }

        var portNumber = DefaultPort;
        if (port is not null &&
            (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out portNumber) ||
             portNumber is < 1 or > 65535))
        {
            throw new InvalidOperationException($"{PortVariable} must be a port number from 1 to 65535.");
        }

        host ??= DefaultHost;
        database ??= DefaultDatabase;
        RejectControlCharacters(host, HostVariable);
        RejectControlCharacters(database, NameVariable);
        RejectControlCharacters(user, UserVariable);
        RejectControlCharacters(password, PasswordVariable);

        return "Host=" + Quote(host) +
               ";Port=" + portNumber.ToString(CultureInfo.InvariantCulture) +
               ";Database=" + Quote(database) +
               ";Username=" + Quote(user) +
               ";Password=" + Quote(password);
    }

    /// <summary>The connection string already stored in the settings file, if any.</summary>
    public static string? ReadConnectionString(string? settingsJson)
    {
        var root = ParseRoot(settingsJson);
        if (!root.TryGetPropertyValue(ConnectionStringsSection, out var section) || section is null)
        {
            return null;
        }

        if (section is not JsonObject sectionObject)
        {
            throw new InvalidOperationException("Settings file ConnectionStrings must be an object.");
        }

        if (!sectionObject.TryGetPropertyValue(ConnectionStringName, out var value) || value is null)
        {
            return null;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            throw new InvalidOperationException(
                $"Settings file {ConnectionStringsSection}:{ConnectionStringName} must be a string.");
        }

        return Clean(text);
    }

    /// <summary>The settings file content with the connection string set; everything else is kept.</summary>
    public static string WithConnectionString(string? settingsJson, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var root = ParseRoot(settingsJson);
        if (root.TryGetPropertyValue(ConnectionStringsSection, out var existing) && existing is not null)
        {
            if (existing is not JsonObject sectionObject)
            {
                throw new InvalidOperationException("Settings file ConnectionStrings must be an object.");
            }

            sectionObject[ConnectionStringName] = connectionString;
        }
        else
        {
            root[ConnectionStringsSection] = new JsonObject { [ConnectionStringName] = connectionString };
        }

        return root.ToJsonString(WriteOptions);
    }

    /// <summary>"yes" or "no" (any case) from the command line; null when not given.</summary>
    public static bool? ParseNetworkAccess(string? option)
    {
        var value = Clean(option);
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException("--network-access must be yes or no.");
    }

    /// <summary>The network-access choice remembered by an earlier install, if any.</summary>
    public static bool? ReadNetworkAccess(string? settingsJson)
    {
        var root = ParseRoot(settingsJson);
        if (!root.TryGetPropertyValue(InstallerSection, out var section) || section is null)
        {
            return null;
        }

        if (section is not JsonObject sectionObject)
        {
            throw new InvalidOperationException($"Settings file {InstallerSection} must be an object.");
        }

        if (!sectionObject.TryGetPropertyValue(NetworkAccessName, out var value) || value is null)
        {
            return null;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out var enabled))
        {
            throw new InvalidOperationException(
                $"Settings file {InstallerSection}:{NetworkAccessName} must be true or false.");
        }

        return enabled;
    }

    /// <summary>The settings file content with the network-access choice remembered; everything else is kept.</summary>
    public static string WithNetworkAccess(string? settingsJson, bool enabled)
    {
        var root = ParseRoot(settingsJson);
        if (root.TryGetPropertyValue(InstallerSection, out var existing) && existing is not null)
        {
            if (existing is not JsonObject sectionObject)
            {
                throw new InvalidOperationException($"Settings file {InstallerSection} must be an object.");
            }

            sectionObject[NetworkAccessName] = enabled;
        }
        else
        {
            root[InstallerSection] = new JsonObject { [NetworkAccessName] = enabled };
        }

        return root.ToJsonString(WriteOptions);
    }

    /// <summary>The Server's listening port from the command line; null when not given.</summary>
    public static int? ParseServerPort(string? option)
    {
        var value = Clean(option);
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException("--port must be a number from 1 to 65535.");
        }

        if (Array.IndexOf(ReservedPorts, port) >= 0)
        {
            throw new InvalidOperationException(
                $"--port {port} is used by another WellSite AutoPilot service ({string.Join(", ", ReservedPorts)}).");
        }

        return port;
    }

    /// <summary>The Server port remembered by an earlier install, if any.</summary>
    public static int? ReadServerPort(string? settingsJson)
    {
        var root = ParseRoot(settingsJson);
        if (!root.TryGetPropertyValue(InstallerSection, out var section) || section is null)
        {
            return null;
        }

        if (section is not JsonObject sectionObject)
        {
            throw new InvalidOperationException($"Settings file {InstallerSection} must be an object.");
        }

        if (!sectionObject.TryGetPropertyValue(ServerPortName, out var value) || value is null)
        {
            return null;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Settings file {InstallerSection}:{ServerPortName} must be a port number from 1 to 65535.");
        }

        return port;
    }

    /// <summary>The settings file content with the Server port remembered; everything else is kept.</summary>
    public static string WithServerPort(string? settingsJson, int port)
    {
        var root = ParseRoot(settingsJson);
        if (root.TryGetPropertyValue(InstallerSection, out var existing) && existing is not null)
        {
            if (existing is not JsonObject sectionObject)
            {
                throw new InvalidOperationException($"Settings file {InstallerSection} must be an object.");
            }

            sectionObject[ServerPortName] = port;
        }
        else
        {
            root[InstallerSection] = new JsonObject { [ServerPortName] = port };
        }

        return root.ToJsonString(WriteOptions);
    }

    /// <summary>Removes every occurrence of the given secrets so output is safe to print.</summary>
    public static string Redact(string text, params string?[] secrets)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                text = text.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return text;
    }

    private static JsonObject ParseRoot(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(settingsJson) as JsonObject ??
                throw new InvalidOperationException("Settings file must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Settings file is not valid JSON: " + exception.Message, exception);
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void RejectControlCharacters(string value, string name)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                throw new InvalidOperationException($"{name} must not contain control characters.");
            }
        }
    }

    /// <summary>Plain words stay readable; anything else is wrapped in double quotes with embedded quotes doubled.</summary>
    private static string Quote(string value)
    {
        var plain = value.Length > 0;
        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'))
            {
                plain = false;
                break;
            }
        }

        if (plain)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            if (character == '"')
            {
                builder.Append('"');
            }

            builder.Append(character);
        }

        return builder.Append('"').ToString();
    }
}
