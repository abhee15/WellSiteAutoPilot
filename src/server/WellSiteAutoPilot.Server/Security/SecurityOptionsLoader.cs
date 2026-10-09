using System.Text.Json;

namespace WellSiteAutoPilot.Server.Security;

public static class SecurityOptionsLoader
{
    private const string SettingsFileName = "server.settings.json";

    public static SecurityOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new SecurityOptions
        {
            AuthenticationMode =
                configuration[
                    $"{SecurityOptions.SectionName}:AuthenticationMode"] ??
                SecurityOptions.NegotiateAuthenticationMode,
            BootstrapAdministrators = configuration
                .GetSection(
                    $"{SecurityOptions.SectionName}:BootstrapAdministrators")
                .GetChildren()
                .Select(item => item.Value)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        if (options.BootstrapAdministrators.Length > 0)
        {
            return options;
        }

        var settingsPath = GetProgramDataSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return options;
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(settingsPath));

        if (!document.RootElement.TryGetProperty(
                SecurityOptions.SectionName,
                out var securityElement) ||
            !securityElement.TryGetProperty(
                nameof(SecurityOptions.BootstrapAdministrators),
                out var administratorsElement) ||
            administratorsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"WellSite AutoPilot security settings file '{settingsPath}' is missing Security:BootstrapAdministrators.");
        }

        options.BootstrapAdministrators = administratorsElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return options;
    }

    public static string GetProgramDataSettingsPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        return Path.Combine(
            root,
            "Weatherford",
            "WellSite AutoPilot",
            SettingsFileName);
    }
}
