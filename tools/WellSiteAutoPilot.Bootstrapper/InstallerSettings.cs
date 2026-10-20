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
    public const int DefaultServerPort = 5443;

    // The Server reads Tls:Thumbprint at startup and serves HTTPS with that certificate from the
    // LocalMachine\My store. Installer:CertificateSource records how it got there, so an upgrade can
    // renew a self-signed certificate it created and refuse to invent one for a certificate the
    // operator chose.
    public const string TlsSection = "Tls";
    public const string ThumbprintName = "Thumbprint";
    public const string CertificateSourceName = "CertificateSource";
    public const string PfxPasswordVariable = "WSA_CERT_PFX_PASSWORD";

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

    /// <summary>A SHA-1 thumbprint as pasted from the certificate dialog: spaces and the hidden
    /// direction marks Windows inserts are removed and the result is upper case hex.</summary>
    public static string NormalizeThumbprint(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (char.IsWhiteSpace(character) || character is '\u200e' or '\u200f' or '\u202a' or '\u202c')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        var thumbprint = builder.ToString();
        if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                "A certificate thumbprint is 40 hexadecimal characters (SHA-1). Copy it from the Details tab of the certificate.");
        }

        return thumbprint;
    }

    /// <summary>
    /// Decides where the HTTPS certificate comes from. At most one source may be requested. Nothing
    /// requested keeps the certificate an earlier install stored; a first install with nothing requested
    /// generates a self-signed certificate, because the installed Server only serves HTTPS.
    /// </summary>
    public static CertificateChoice ChooseCertificate(
        string? thumbprintOption, string? pfxOption, bool selfSignedOption, string? storedThumbprint)
    {
        var thumbprint = Clean(thumbprintOption);
        var pfx = Clean(pfxOption);
        var requested = (thumbprint is null ? 0 : 1) + (pfx is null ? 0 : 1) + (selfSignedOption ? 1 : 0);
        if (requested > 1)
        {
            throw new InvalidOperationException(
                "Choose one certificate source: --cert-thumbprint, --cert-pfx or --cert-self-signed.");
        }

        if (thumbprint is not null)
        {
            return new CertificateChoice(CertificateKind.Store, NormalizeThumbprint(thumbprint));
        }

        if (pfx is not null)
        {
            return new CertificateChoice(CertificateKind.Pfx, pfx);
        }

        if (selfSignedOption)
        {
            return new CertificateChoice(CertificateKind.SelfSigned, null);
        }

        return storedThumbprint is not null
            ? new CertificateChoice(CertificateKind.Stored, NormalizeThumbprint(storedThumbprint))
            : new CertificateChoice(CertificateKind.SelfSigned, null);
    }

    /// <summary>The certificate thumbprint and source an earlier install remembered, if any.</summary>
    public static (string? Thumbprint, string? Source) ReadCertificate(string? settingsJson)
    {
        var root = ParseRoot(settingsJson);
        return (ReadString(root, TlsSection, ThumbprintName), ReadString(root, InstallerSection, CertificateSourceName));
    }

    /// <summary>The settings file content with the certificate remembered; everything else is kept.</summary>
    public static string WithCertificate(string? settingsJson, string thumbprint, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var root = ParseRoot(settingsJson);
        SetString(root, TlsSection, ThumbprintName, NormalizeThumbprint(thumbprint));
        SetString(root, InstallerSection, CertificateSourceName, source);
        return root.ToJsonString(WriteOptions);
    }

    // The Windows account the Integration Gateway runs as, and therefore the identity ForeSite and CygNet see.
    // LocalService is anonymous on the network, so a real provider needs a domain or group-managed service
    // account. The choice is remembered so an upgrade keeps it; a password is never stored.
    public const string GatewayAccountName = "GatewayAccount";
    public const string GatewayAccountPasswordVariable = "WSA_GATEWAY_ACCOUNT_PASSWORD";
    public const string DefaultGatewayAccount = @"NT AUTHORITY\LocalService";

    /// <summary>DOMAIN\name or .\name: letters, digits, dot, dash and underscore, plus a trailing $ on the name.</summary>
    private static bool IsAccountShape(string value)
    {
        var parts = value.Split('\\');
        if (parts.Length != 2) return false;
        var (domain, name) = (parts[0], parts[1]);
        static bool Plain(string text, bool allowDollar) =>
            text.Length is >= 1 and <= 63 && char.IsAsciiLetterOrDigit(text[0]) &&
            text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' || allowDollar && c == '$');
        return (domain == "." || Plain(domain, false)) && Plain(name, true);
    }

    public static bool IsDefaultGatewayAccount(string account) =>
        string.Equals(account, DefaultGatewayAccount, StringComparison.OrdinalIgnoreCase);

    /// <summary>A group-managed service account has a name ending in $ and Windows holds its password.</summary>
    public static bool IsManagedServiceAccount(string account) => account.EndsWith('$');

    /// <summary>A Gateway account as DOMAIN\name (or .\name for a local account); refuses anything else.</summary>
    public static string ParseGatewayAccount(string? option)
    {
        var value = Clean(option);
        if (value is null) throw new InvalidOperationException("--gateway-account needs an account such as CORP\\svc-wsa-gateway$.");
        if (IsDefaultGatewayAccount(value)) return DefaultGatewayAccount;
        if (!IsAccountShape(value))
        {
            throw new InvalidOperationException(
                "--gateway-account must look like DOMAIN\\account (use .\\account for a local account), " +
                "or be left out to run as LocalService.");
        }

        return value;
    }

    /// <summary>
    /// The account the Gateway should run as: the one requested now, else the one an earlier install remembered,
    /// else LocalService. Asking for both a named account and the default is a contradiction.
    /// </summary>
    public static (string Account, bool Changed) ChooseGatewayAccount(string? requested, bool useDefault, string? stored)
    {
        if (useDefault && Clean(requested) is not null)
        {
            throw new InvalidOperationException("Use either --gateway-account or --gateway-account-default, not both.");
        }

        var effectiveStored = stored is null ? DefaultGatewayAccount : ParseGatewayAccount(stored);
        var chosen = useDefault ? DefaultGatewayAccount
            : Clean(requested) is null ? effectiveStored : ParseGatewayAccount(requested);
        return (chosen, !string.Equals(chosen, effectiveStored, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A password is needed for a named account that Windows does not manage the password of.</summary>
    public static bool GatewayAccountNeedsPassword(string account) =>
        !IsDefaultGatewayAccount(account) && !IsManagedServiceAccount(account);

    public static string? ReadGatewayAccount(string? settingsJson) =>
        ReadString(ParseRoot(settingsJson), InstallerSection, GatewayAccountName);

    public static string WithGatewayAccount(string? settingsJson, string account)
    {
        var root = ParseRoot(settingsJson);
        SetString(root, InstallerSection, GatewayAccountName, ParseGatewayAccount(account));
        return root.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// A security-policy export (secedit) with the account's SID allowed to log on as a service, and whether
    /// anything changed. The right is what lets Windows start a service under that account.
    /// </summary>
    public static (string Content, bool Changed) WithServiceLogonRight(string policyExport, string sid)
    {
        ArgumentNullException.ThrowIfNull(policyExport);
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        var entry = sid.StartsWith('*') ? sid : "*" + sid;
        var lines = policyExport.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var header = lines.FindIndex(line => line.Trim().Equals("[Privilege Rights]", StringComparison.OrdinalIgnoreCase));
        if (header < 0)
        {
            lines.AddRange(["[Privilege Rights]", "SeServiceLogonRight = " + entry]);
            return (string.Join("\r\n", lines) + "\r\n", true);
        }

        for (var index = header + 1; index < lines.Count && !lines[index].TrimStart().StartsWith('['); index++)
        {
            var line = lines[index];
            if (!line.TrimStart().StartsWith("SeServiceLogonRight", StringComparison.OrdinalIgnoreCase)) continue;
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            var holders = equals < 0 ? [] : line[(equals + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (holders.Contains(entry, StringComparer.OrdinalIgnoreCase)) return (policyExport, false);
            lines[index] = "SeServiceLogonRight = " + string.Join(",", holders.Append(entry));
            return (string.Join("\r\n", lines), true);
        }

        lines.Insert(header + 1, "SeServiceLogonRight = " + entry);
        return (string.Join("\r\n", lines), true);
    }

    private static string? ReadString(JsonObject root, string section, string name)
    {
        if (!root.TryGetPropertyValue(section, out var node) || node is null)
        {
            return null;
        }

        if (node is not JsonObject sectionObject)
        {
            throw new InvalidOperationException($"Settings file {section} must be an object.");
        }

        if (!sectionObject.TryGetPropertyValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            throw new InvalidOperationException($"Settings file {section}:{name} must be text.");
        }

        return Clean(text);
    }

    private static void SetString(JsonObject root, string section, string name, string value)
    {
        if (root.TryGetPropertyValue(section, out var existing) && existing is not null)
        {
            if (existing is not JsonObject sectionObject)
            {
                throw new InvalidOperationException($"Settings file {section} must be an object.");
            }

            sectionObject[name] = value;
        }
        else
        {
            root[section] = new JsonObject { [name] = value };
        }
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

    // Keys the internal callers (Server, worker) present to the Integration Gateway. Each service has its own
    // file under ProgramData, readable only by that service's account. The Gateway file holds SHA-256 verifiers.
    public const string ServerKeysFile = "server.keys.json";
    public const string WorkerKeysFile = "worker.keys.json";
    public const string GatewayKeysFile = "gateway.keys.json";

    /// <summary>A fresh 256-bit key as 64 hexadecimal characters.</summary>
    public static string NewServiceKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>The verifier the Gateway stores for a key: base64 of its SHA-256. Must match the Gateway's check.</summary>
    public static string ServiceKeyVerifier(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>The key file for the Server or the worker: <c>{"Security":{"ServiceKey":"..."}}</c>.</summary>
    public static string ServiceKeyFileContent(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new JsonObject { ["Security"] = new JsonObject { ["ServiceKey"] = key } }.ToJsonString(WriteOptions);
    }

    /// <summary>The key stored in a Server or worker key file, or null when there is none.</summary>
    public static string? ReadServiceKey(string? json)
    {
        var root = ParseRoot(json);
        return root["Security"] is JsonObject security && security["ServiceKey"] is JsonValue value &&
               value.TryGetValue<string>(out var key) ? Clean(key) : null;
    }

    /// <summary>The Gateway file: verifiers for the Server and the worker, never the keys themselves.</summary>
    public static string GatewayKeyFileContent(string serverKey, string workerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerKey);
        return new JsonObject
        {
            ["Security"] = new JsonObject
            {
                ["Callers"] = new JsonObject
                {
                    ["server"] = new JsonObject { ["Verifier"] = ServiceKeyVerifier(serverKey) },
                    ["worker"] = new JsonObject { ["Verifier"] = ServiceKeyVerifier(workerKey) }
                }
            }
        }.ToJsonString(WriteOptions);
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

internal enum CertificateKind
{
    /// <summary>A certificate already in LocalMachine\My, named by its thumbprint.</summary>
    Store,

    /// <summary>A PFX file to import into LocalMachine\My.</summary>
    Pfx,

    /// <summary>A certificate the installer generates (trial use).</summary>
    SelfSigned,

    /// <summary>The certificate an earlier install stored.</summary>
    Stored
}

internal sealed record CertificateChoice(CertificateKind Kind, string? Value);
