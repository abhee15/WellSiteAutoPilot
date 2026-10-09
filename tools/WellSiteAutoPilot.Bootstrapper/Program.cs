using System.Diagnostics;
using System.Text.Json;

return Bootstrapper.Run(args);

internal static class Bootstrapper
{
    private const string NatsServiceName = "Weatherford.WellSiteAutoPilot.Nats";
    private const string ServerServiceName = "Weatherford.WellSiteAutoPilot.Server";
    private const string GatewayServiceName = "Weatherford.WellSiteAutoPilot.IntegrationGateway";
    private const string WorkerServiceName = "Weatherford.WellSiteAutoPilot.Worker.DotNet";

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
        ConfigureBootstrapAdministrator(
            programDataRoot,
            GetOption(args, "--bootstrap-admin"));

        var natsDataRoot = Path.Combine(programDataRoot, "NATS", "JetStream");
        Directory.CreateDirectory(natsDataRoot);
        GrantLocalServiceModifyAccess(Path.Combine(programDataRoot, "NATS"));

        var services = new[]
        {
            new ServiceDefinition(
                NatsServiceName,
                "WellSite AutoPilot NATS",
                Path.Combine(installRoot, "Infrastructure", "NATSHost", "WellSiteAutoPilot.NatsHost.exe"),
                string.Empty),
            new ServiceDefinition(
                GatewayServiceName,
                "WellSite AutoPilot Integration Gateway",
                Path.Combine(installRoot, "IntegrationGateway", "WellSiteAutoPilot.IntegrationGateway.exe"),
                "--urls http://127.0.0.1:5081"),
            new ServiceDefinition(
                WorkerServiceName,
                "WellSite AutoPilot .NET Runtime",
                Path.Combine(installRoot, "Workers", "DotNet", "WellSiteAutoPilot.Worker.DotNet.exe"),
                "--urls http://127.0.0.1:5082"),
            new ServiceDefinition(
                ServerServiceName,
                "WellSite AutoPilot Server",
                Path.Combine(installRoot, "Server", "WellSiteAutoPilot.Server.exe"),
                "--urls http://127.0.0.1:5080")
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
                @"NT AUTHORITY\LocalService",
                "DisplayName=",
                service.DisplayName);

            RunSc(
                "description",
                service.ServiceName,
                $"Weatherford {service.DisplayName}.");

            RunSc("start", service.ServiceName);
            WaitForState(service.ServiceName, "RUNNING", TimeSpan.FromSeconds(30));
        }

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

        if (existingIdentities.Count > 0)
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

            GrantLocalServiceReadAccess(settingsPath);
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
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var temporaryPath =
            settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        File.WriteAllText(temporaryPath, payload);
        File.Move(temporaryPath, settingsPath, overwrite: true);

        GrantLocalServiceReadAccess(settingsPath);
    }

    private static IReadOnlyCollection<string> ReadBootstrapAdministrators(
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

    private static string ValidateBootstrapIdentity(string identity)
    {
        var value = identity.Trim();

        if (value.Length is < 1 or > 256 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Bootstrap administrator identity is invalid.");
        }

        if (!value.Contains('\\', StringComparison.Ordinal) &&
            !value.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Bootstrap administrator must be a Windows DOMAIN\\user, MACHINE\\user, or UPN identity.");
        }

        return value;
    }

    private static void GrantLocalServiceReadAccess(string path)
    {
        var result = RunProcessAllowFailure(
            "icacls.exe",
            path,
            "/grant:r",
            @"NT AUTHORITY\LOCAL SERVICE:R");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to grant LocalService read access to {path}: " +
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
        var result = RunScAllowFailure("query", serviceName);
        return result.ExitCode == 0;
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
            "  install-services --install-root <path> [--bootstrap-admin <WindowsIdentity>]");
        Console.WriteLine(
            "    --bootstrap-admin is required on a fresh installation and preserved on upgrades.");
        Console.WriteLine("  uninstall-services");
    }

    private sealed record ServiceDefinition(
        string ServiceName,
        string DisplayName,
        string ExecutablePath,
        string Arguments);

    private sealed record ScResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
