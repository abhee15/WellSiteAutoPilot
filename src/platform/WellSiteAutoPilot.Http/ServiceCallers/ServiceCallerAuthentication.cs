using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WellSiteAutoPilot.Http.ServiceCallers;

/// <summary>Stored SHA-256 verifiers, by caller name. Configuration section Security:Callers:{name}:Verifier.</summary>
public sealed class ServiceCallerOptions
{
    public IDictionary<string, string> Verifiers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public static class ServiceCallerAuthentication
{
    public const string SectionName = "Security:Callers";

    /// <summary>
    /// Authenticates internal callers by <c>Authorization: WSA-Service {name}:{key}</c> and defines the policies
    /// that say which caller may use which route. Startup fails when a verifier is missing or a caller is unknown,
    /// so the Gateway can never run open by misconfiguration.
    /// </summary>
    public static IServiceCollection AddServiceCallerAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ServiceCallerOptions();
        var section = configuration.GetSection(SectionName);
        foreach (var child in section.GetChildren())
        {
            var name = ServiceCaller.All.FirstOrDefault(known => string.Equals(known, child.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"{SectionName}:{child.Key} is not a known caller. Known callers: {string.Join(", ", ServiceCaller.All)}.");
            var verifier = child["Verifier"];
            if (!string.IsNullOrWhiteSpace(verifier)) options.Verifiers[name] = verifier.Trim();
        }

        foreach (var caller in ServiceCaller.All)
            if (!options.Verifiers.ContainsKey(caller))
                throw new InvalidOperationException(
                    $"The Integration Gateway needs {SectionName}:{caller}:Verifier. The installer creates it; " +
                    "for development supply it through configuration.");

        services.AddSingleton(Options.Create(options));
        services.AddAuthentication(ServiceCaller.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ServiceCallerAuthenticationHandler>(ServiceCaller.Scheme, configureOptions: null);
        services.AddAuthorizationBuilder()
            .AddPolicy(ServiceCaller.ServerPolicy, policy => policy
                .AddAuthenticationSchemes(ServiceCaller.Scheme)
                .RequireClaim(ServiceCaller.ClaimType, ServiceCaller.Server))
            .AddPolicy(ServiceCaller.DataReadPolicy, policy => policy
                .AddAuthenticationSchemes(ServiceCaller.Scheme)
                .RequireClaim(ServiceCaller.ClaimType, ServiceCaller.Server, ServiceCaller.Worker));
        return services;
    }
}
