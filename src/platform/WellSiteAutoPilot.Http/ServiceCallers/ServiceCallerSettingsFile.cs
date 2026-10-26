using Microsoft.Extensions.Configuration;

namespace WellSiteAutoPilot.Http.ServiceCallers;

public static class ServiceCallerSettingsFile
{
    /// <summary>
    /// The machine-wide file the installer writes for one service, for example <c>gateway.keys.json</c>. It holds
    /// only that service's key or verifiers and is readable only by that service's account. Optional, so
    /// development and tests can supply the same settings through configuration instead.
    /// </summary>
    public static string PathFor(string fileName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Weatherford", "WellSite AutoPilot", fileName);

    public static IConfigurationBuilder AddServiceCallerSettingsFile(this IConfigurationBuilder builder, string fileName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddJsonFile(PathFor(fileName), optional: true, reloadOnChange: false);
        // Developer services can run alongside an installed configuration. Only
        // explicit credential overrides win here; other configuration keeps its precedence.
        string[] credentialKeys = ["Security:ServiceKey", "Security:Callers:server:Verifier",
            "Security:Callers:worker:Verifier", "Messaging:Nats:Token"];
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in credentialKeys)
            if (Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal)) is { } value)
                overrides[key] = value;
        return builder.AddInMemoryCollection(overrides);
    }
}
