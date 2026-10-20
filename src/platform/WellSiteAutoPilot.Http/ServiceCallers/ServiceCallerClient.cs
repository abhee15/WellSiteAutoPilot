using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WellSiteAutoPilot.Http.ServiceCallers;

public static class ServiceCallerClient
{
    public const string KeyConfigurationName = "Security:ServiceKey";

    /// <summary>
    /// Makes every request of this client carry the caller's key. Startup fails when the key is absent, so a
    /// service cannot start and then fail every Gateway call.
    /// </summary>
    public static IHttpClientBuilder AddServiceCallerKey(
        this IHttpClientBuilder builder, IConfiguration configuration, string callerName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!ServiceCaller.All.Contains(callerName, StringComparer.Ordinal))  // lower case, as the Gateway expects
            throw new ArgumentException("Unknown caller.", nameof(callerName));
        var key = configuration[KeyConfigurationName];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"{KeyConfigurationName} is missing. The installer creates it; for development supply it through configuration.");
        var credential = new AuthenticationHeaderValue(ServiceCaller.Scheme, callerName + ":" + key.Trim());
        return builder.ConfigureHttpClient(client => client.DefaultRequestHeaders.Authorization = credential);
    }
}
