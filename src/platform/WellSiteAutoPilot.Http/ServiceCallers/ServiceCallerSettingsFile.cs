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
        return builder.AddJsonFile(PathFor(fileName), optional: true, reloadOnChange: false);
    }
}
