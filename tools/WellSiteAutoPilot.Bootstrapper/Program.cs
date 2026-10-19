using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

return Bootstrapper.Run(args);

internal static class Bootstrapper
{
    private const string NatsServiceName = "Weatherford.WellSiteAutoPilot.Nats";
    private const string ServerServiceName = "Weatherford.WellSiteAutoPilot.Server";
    private const string GatewayServiceName = "Weatherford.WellSiteAutoPilot.IntegrationGateway";
    private const string WorkerServiceName = "Weatherford.WellSiteAutoPilot.Worker.DotNet";
    private const string FirewallRuleName = "WellSiteAutoPilot.Server";

    // LocalService has no network identity: it presents anonymous credentials, so a service running
    // as LocalService cannot complete a Negotiate (Kerberos/NTLM) sign-in and browsers prompt forever.
    // The Server authenticates browser users and reads the directory, so it runs as NetworkService,
    // which uses the machine account. The other services need no network identity.
    private const string LocalServiceAccount = @"NT AUTHORITY\LocalService";
    private const string NetworkServiceAccount = @"NT AUTHORITY\NetworkService";
    private static readonly JsonSerializerOptions SettingsJsonOptions =
        new()
        {
            WriteIndented = true
        };

    public static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("WellSite AutoPilot bootstrapper service operations require Windows.");
            return 2;
        }

        if (args.Length == 0)
        {
            WriteUsage();
            return 2;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "install-services" => InstallServices(args),
                "uninstall-services" => UninstallServices(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int InstallServices(string[] args)
    {
        var installRoot = GetOption(args, "--install-root");
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException("install-services requires --install-root <path>.");
        }

        installRoot = Path.GetFullPath(installRoot);

        var programDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Weatherford",
            "WellSite AutoPilot");

        Directory.CreateDirectory(programDataRoot);
        var settingsPath = Path.Combine(programDataRoot, "server.settings.json");

        // Resolve the database first so a missing or malformed setting stops the install before
        // anything is written or any service is touched.
        var database = ResolveDatabaseConnection(settingsPath);
        var network = ResolveNetworkAccess(
            settingsPath, GetOption(args, "--network-access"), GetOption(args, "--port"));

        ConfigureBootstrapAdministrator(
            programDataRoot,
            GetOption(args, "--bootstrap-admin"));
        StoreInstallerSettings(settingsPath, database, network);
        RunDatabaseMigrator(installRoot, database.ConnectionString);

        var natsDataRoot = Path.Combine(programDataRoot, "NATS", "JetStream");
        Directory.CreateDirectory(natsDataRoot);
        GrantLocalServiceModifyAccess(Path.Combine(programDataRoot, "NATS"));

        var services = new[]
        {
            new ServiceDefinition(
                NatsServiceName,
                "WellSite AutoPilot NATS",
                Path.Combine(installRoot, "Infrastructure", "NATSHost", "WellSiteAutoPilot.NatsHost.exe"),
                string.Empty,
                LocalServiceAccount),
            new ServiceDefinition(
                GatewayServiceName,
                "WellSite AutoPilot Integration Gateway",
                Path.Combine(installRoot, "IntegrationGateway", "WellSiteAutoPilot.IntegrationGateway.exe"),
                "--urls http://127.0.0.1:5081",
                LocalServiceAccount),
            new ServiceDefinition(
                WorkerServiceName,
                "WellSite AutoPilot .NET Runtime",
                Path.Combine(installRoot, "Workers", "DotNet", "WellSiteAutoPilot.Worker.DotNet.exe"),
                "--urls http://127.0.0.1:5082",
                LocalServiceAccount),
            new ServiceDefinition(
                ServerServiceName,
                "WellSite AutoPilot Server",
                Path.Combine(installRoot, "Server", "WellSiteAutoPilot.Server.exe"),
                // Only the Server is reachable from the network; NATS, the Gateway and the worker stay on loopback.
                network.Enabled
                    ? $"--urls http://*:{network.Port}"
                    : $"--urls http://127.0.0.1:{network.Port}",
                NetworkServiceAccount)
        };

        foreach (var service in services)
        {
            if (!File.Exists(service.ExecutablePath))
            {
                throw new FileNotFoundException(
                    $"Required service executable was not found: {service.ExecutablePath}",
                    service.ExecutablePath);
            }
        }

        foreach (var service in services)
        {
            RemoveServiceIfPresent(service.ServiceName);

            var binPath = string.IsNullOrWhiteSpace(service.Arguments)
                ? $"\"{service.ExecutablePath}\""
                : $"\"{service.ExecutablePath}\" {service.Arguments}";

            RunSc(
                "create",
                service.ServiceName,
                "binPath=",
                binPath,
                "start=",
                "auto",
                "obj=",
                service.Account,
                "DisplayName=",
                service.DisplayName);

            RunSc(
                "description",
                service.ServiceName,
                $"Weatherford {service.DisplayName}.");

            RunSc("start", service.ServiceName);
            WaitForState(service.ServiceName, "RUNNING", TimeSpan.FromSeconds(30));
        }

        ConfigureFirewall(
            services.Single(service => service.ServiceName == ServerServiceName).ExecutablePath,
            network.Enabled,
            network.Port);

        Console.WriteLine(network.Enabled
            ? $"WellSite AutoPilot is reachable from the network on port {network.Port} (Domain and Private networks)."
            : $"WellSite AutoPilot is reachable from this computer only, on port {network.Port}.");
        Console.WriteLine("WellSite AutoPilot Windows services installed and started.");
        return 0;
    }

    private static int UninstallServices()
    {
        foreach (var serviceName in new[]
        {
            ServerServiceName,
            WorkerServiceName,
            GatewayServiceName,
            NatsServiceName
        })
        {
            RemoveServiceIfPresent(serviceName);
        }

        RunNetshAllowFailure($"advfirewall firewall delete rule name={FirewallRuleName}");

        Console.WriteLine("WellSite AutoPilot Windows services removed.");
        return 0;
    }

    private static void ConfigureBootstrapAdministrator(
        string programDataRoot,
        string? requestedIdentity)
    {
        var settingsPath = Path.Combine(
            programDataRoot,
            "server.settings.json");

        var existingIdentities = ReadBootstrapAdministrators(settingsPath);

        if (existingIdentities.Length > 0)
        {
            if (!string.IsNullOrWhiteSpace(requestedIdentity))
            {
                var normalizedRequested = ValidateBootstrapIdentity(
                    requestedIdentity);

                if (!existingIdentities.Contains(
                        normalizedRequested,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "A different bootstrap administrator is already configured. " +
                        "Change administrator access through WellSite AutoPilot after installation instead of replacing bootstrap settings during an upgrade.");
                }
            }

            SecureSettingsFile(settingsPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(requestedIdentity))
        {
            throw new InvalidOperationException(
                "Fresh installation requires --bootstrap-admin <WindowsIdentity>. " +
                "Use DOMAIN\\user, MACHINE\\user, or a Windows UPN.");
        }

        var bootstrapIdentity = ValidateBootstrapIdentity(
            requestedIdentity);

        if (OperatingSystem.IsWindows())
        {
            VerifyBootstrapAccountResolves(bootstrapIdentity);
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                Security = new
                {
                    BootstrapAdministrators = new[]
                    {
                        bootstrapIdentity
                    }
                }
            },
            SettingsJsonOptions);

        var temporaryPath =
            settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        File.WriteAllText(temporaryPath, payload);
        File.Move(temporaryPath, settingsPath, overwrite: true);

        SecureSettingsFile(settingsPath);
    }

    private static string[] ReadBootstrapAdministrators(
        string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(settingsPath));

        if (!document.RootElement.TryGetProperty(
                "Security",
                out var securityElement) ||
            !securityElement.TryGetProperty(
                "BootstrapAdministrators",
                out var administratorsElement) ||
            administratorsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Security settings file is invalid: {settingsPath}");
        }

        return administratorsElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// A typo in the bootstrap account would otherwise surface only as an unexplained
    /// access failure at first sign-in. Reject an account Windows cannot find. When the
    /// directory cannot be reached (offline or VPN-less install) only warn. Disposable
    /// test installs may set WSA_ALLOW_UNRESOLVED_BOOTSTRAP_ADMIN=1 to accept a fake account.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void VerifyBootstrapAccountResolves(string identity)
    {
        try
        {
            _ = new NTAccount(identity).Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("WSA_ALLOW_UNRESOLVED_BOOTSTRAP_ADMIN"),
                    "1",
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"WARNING: bootstrap administrator '{identity}' was not found; accepted because WSA_ALLOW_UNRESOLVED_BOOTSTRAP_ADMIN=1.");
                return;
            }

            throw new InvalidOperationException(
                $"Bootstrap administrator '{identity}' was not found by Windows. " +
                "Check the spelling and use DOMAIN\\user, MACHINE\\user, or a UPN. " +
                "For a domain account, make sure this machine can reach the domain.");
        }
        catch (Exception ex) when (ex is SystemException or ArgumentException)
        {
            Console.Error.WriteLine(
                $"WARNING: could not verify bootstrap administrator '{identity}' ({ex.GetType().Name}). " +
                "Continuing; confirm the account name before first sign-in.");
        }
    }

    private static string ValidateBootstrapIdentity(string identity)
    {
        var value = identity.Trim();

        if (value.Length is < 1 or > 256 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Bootstrap administrator identity is invalid.");
        }

        if (!value.Contains('\\') &&
            !value.Contains('@'))
        {
            throw new InvalidOperationException(
                "Bootstrap administrator must be a Windows DOMAIN\\user, MACHINE\\user, or UPN identity.");
        }

        return value;
    }

    /// <summary>
    /// The settings file can hold the database password. Only SYSTEM and Administrators get full
    /// control and only the Server's account (NetworkService) can read it; inherited access is removed,
    /// and any LocalService entry left by an earlier version is withdrawn.
    /// </summary>
    private static void SecureSettingsFile(string path)
    {
        var result = RunProcessAllowFailure(
            "icacls.exe",
            path,
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-18:F",
            "*S-1-5-32-544:F",
            "*S-1-5-20:R",
            "/remove:g",
            "*S-1-5-19");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to secure bootstrap settings at {path}: " +
                $"{result.StandardOutput} {result.StandardError}".Trim());
        }
    }

    private static void GrantLocalServiceModifyAccess(string path)
    {
        var result = RunProcessAllowFailure(
            "icacls.exe",
            path,
            "/grant",
            @"NT AUTHORITY\LOCAL SERVICE:(OI)(CI)M",
            "/T",
            "/C");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to grant LocalService access to {path}: " +
                $"{result.StandardOutput} {result.StandardError}".Trim());
        }
    }

    private static DatabaseConnection ResolveDatabaseConnection(string settingsPath)
    {
        var requested = InstallerSettings.BuildConnectionString(
            name => Environment.GetEnvironmentVariable(name));
        var stored = File.Exists(settingsPath)
            ? InstallerSettings.ReadConnectionString(File.ReadAllText(settingsPath))
            : null;

        if (requested is null && stored is null)
        {
            throw new InvalidOperationException(
                "No database connection is configured. Provide the PostgreSQL connection with " +
                $"{InstallerSettings.UserVariable} and {InstallerSettings.PasswordVariable} " +
                $"(optionally {InstallerSettings.HostVariable}, {InstallerSettings.PortVariable}, " +
                $"{InstallerSettings.NameVariable}), or a full {InstallerSettings.FullConnectionStringVariable}. " +
                "The database must already exist and the user must be able to create tables in it.");
        }

        var effective = requested ?? stored!;
        return new DatabaseConnection(
            effective,
            Changed: requested is not null &&
                     !string.Equals(requested, stored, StringComparison.Ordinal));
    }

    private static NetworkChoice ResolveNetworkAccess(string settingsPath, string? accessOption, string? portOption)
    {
        var requestedAccess = InstallerSettings.ParseNetworkAccess(accessOption);
        var requestedPort = InstallerSettings.ParseServerPort(portOption);
        var settings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var storedAccess = settings is null ? null : InstallerSettings.ReadNetworkAccess(settings);
        var storedPort = settings is null ? null : InstallerSettings.ReadServerPort(settings);

        // The choices are remembered, so an upgrade that does not mention them never silently widens (or
        // narrows) who can connect or moves the Server. A first install defaults to reachable from the
        // network on port 5080.
        return new NetworkChoice(
            requestedAccess ?? storedAccess ?? true,
            requestedPort ?? storedPort ?? InstallerSettings.DefaultServerPort,
            AccessChanged: requestedAccess is not null && requestedAccess != storedAccess,
            PortChanged: requestedPort is not null && requestedPort != storedPort);
    }

    private static void StoreInstallerSettings(
        string settingsPath, DatabaseConnection database, NetworkChoice network)
    {
        if (!database.Changed && !network.AccessChanged && !network.PortChanged)
        {
            return;
        }

        var updated = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        if (database.Changed)
        {
            updated = InstallerSettings.WithConnectionString(updated, database.ConnectionString);
        }

        if (network.AccessChanged)
        {
            updated = InstallerSettings.WithNetworkAccess(updated, network.Enabled);
        }

        if (network.PortChanged)
        {
            updated = InstallerSettings.WithServerPort(updated, network.Port);
        }

        // Lock the file down before the secret is written, so it is never readable through the
        // permissions inherited from ProgramData, even briefly.
        var temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, string.Empty);
        SecureSettingsFile(temporaryPath);
        File.WriteAllText(temporaryPath, updated!);
        File.Move(temporaryPath, settingsPath, overwrite: true);
        SecureSettingsFile(settingsPath);
        Console.WriteLine("Installer settings stored in the protected machine settings file.");
    }

    /// <summary>
    /// Allows inbound TCP to the Server only (the rule is bound to its executable) on Domain and Private
    /// networks, never Public. Idempotent: any earlier rule is removed first, and turning network access
    /// off removes it.
    /// </summary>
    private static void ConfigureFirewall(string serverExecutable, bool enabled, int port)
    {
        RunNetshAllowFailure($"advfirewall firewall delete rule name={FirewallRuleName}");
        if (!enabled)
        {
            return;
        }

        var result = RunNetshAllowFailure(
            $"advfirewall firewall add rule name={FirewallRuleName} dir=in action=allow protocol=TCP " +
            $"localport={port} program=\"{serverExecutable}\" profile=domain,private enable=yes");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Failed to open the Windows Firewall for the Server: " +
                $"{result.StandardOutput} {result.StandardError}".Trim());
        }
    }

    // netsh needs program="path with spaces" quoted exactly, so the command line is built as one string.
    private static ScResult RunNetshAllowFailure(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScResult(process.ExitCode, standardOutput, standardError);
    }

    private static void RunDatabaseMigrator(string installRoot, string connectionString)
    {
        var migratorPath = Path.Combine(
            installRoot, "Tools", "DatabaseMigrator", "WellSiteAutoPilot.DatabaseMigrator.exe");
        if (!File.Exists(migratorPath))
        {
            throw new FileNotFoundException(
                $"Database migrator was not found: {migratorPath}", migratorPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = migratorPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // The connection reaches the migrator only through its own environment, never the command line.
        process.StartInfo.Environment[InstallerSettings.FullConnectionStringVariable] = connectionString;

        var output = new StringBuilder();
        void Capture(object _, DataReceivedEventArgs received)
        {
            if (received.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(received.Data);
            }
        }

        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                "Database migration did not finish within 5 minutes. Check that PostgreSQL is reachable.");
        }

        process.WaitForExit();

        string text;
        lock (output)
        {
            text = InstallerSettings.Redact(
                output.ToString(),
                connectionString,
                Environment.GetEnvironmentVariable(InstallerSettings.PasswordVariable)).Trim();
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Database migration failed with exit code {process.ExitCode}. Check the PostgreSQL " +
                $"connection settings and that the database exists. {text}".Trim());
        }

        Console.WriteLine(text);
    }

    private static void RemoveServiceIfPresent(string serviceName)
    {
        if (!ServiceExists(serviceName))
        {
            return;
        }

        RunScAllowFailure("stop", serviceName);
        WaitForState(serviceName, "STOPPED", TimeSpan.FromSeconds(30), allowMissing: true);
        RunSc("delete", serviceName);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline && ServiceExists(serviceName))
        {
            Thread.Sleep(500);
        }

        if (ServiceExists(serviceName))
        {
            throw new InvalidOperationException($"Timed out removing Windows service {serviceName}.");
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        // 1060 is "service does not exist". A deleted service that something still holds open answers
        // 1072 ("marked for deletion") until the last handle closes, and creating the same name then
        // fails, so only 1060 counts as gone.
        const int ServiceDoesNotExist = 1060;
        var result = RunScAllowFailure("query", serviceName);
        return result.ExitCode != ServiceDoesNotExist;
    }

    private static void WaitForState(
        string serviceName,
        string expectedState,
        TimeSpan timeout,
        bool allowMissing = false)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = RunScAllowFailure("query", serviceName);

            if (result.ExitCode != 0)
            {
                if (allowMissing)
                {
                    return;
                }

                Thread.Sleep(500);
                continue;
            }

            if (result.StandardOutput.Contains(expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Thread.Sleep(500);
        }

        throw new InvalidOperationException(
            $"Timed out waiting for Windows service {serviceName} to reach {expectedState}.");
    }

    private static ScResult RunSc(params string[] arguments)
    {
        var result = RunScAllowFailure(arguments);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sc.exe {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: " +
                $"{result.StandardOutput} {result.StandardError}".Trim());
        }

        return result;
    }

    private static ScResult RunScAllowFailure(params string[] arguments) =>
        RunProcessAllowFailure("sc.exe", arguments);

    private static ScResult RunProcessAllowFailure(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return new ScResult(process.ExitCode, standardOutput, standardError);
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown bootstrapper command: {command}");
        WriteUsage();
        return 2;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("WellSite AutoPilot Bootstrapper");
        Console.WriteLine(
            "  install-services --install-root <path> [--bootstrap-admin <WindowsIdentity>] [--network-access yes|no] [--port <1-65535>]");
        Console.WriteLine(
            "    --network-access (default yes, then remembered) lets other computers on Domain and Private");
        Console.WriteLine(
            "      networks reach the Server and opens the Windows Firewall for it; no = this computer only.");
        Console.WriteLine(
            "    --port (default 5080, then remembered) is the Server's port; 5081, 5082 and 4222 are taken by other services.");
        Console.WriteLine(
            "    --bootstrap-admin is required on a fresh installation and preserved on upgrades.");
        Console.WriteLine(
            "    The PostgreSQL connection is read from the environment (never the command line):");
        Console.WriteLine(
            "      WSA_DATABASE_USER and WSA_DATABASE_PASSWORD, optionally WSA_DATABASE_HOST,");
        Console.WriteLine(
            "      WSA_DATABASE_PORT and WSA_DATABASE_NAME, or one WSA_DATABASE_CONNECTION_STRING.");
        Console.WriteLine(
            "      Required on a fresh installation; an upgrade keeps the stored connection.");
        Console.WriteLine(
            "      The database must already exist. Migrations run before any service starts.");
        Console.WriteLine("  uninstall-services");
    }

    private sealed record ServiceDefinition(
        string ServiceName,
        string DisplayName,
        string ExecutablePath,
        string Arguments,
        string Account);

    private sealed record DatabaseConnection(string ConnectionString, bool Changed);

    private sealed record NetworkChoice(bool Enabled, int Port, bool AccessChanged, bool PortChanged);

    private sealed record ScResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
