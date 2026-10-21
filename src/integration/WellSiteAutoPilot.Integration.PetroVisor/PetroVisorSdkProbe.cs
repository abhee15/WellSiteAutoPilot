using System.Reflection;

namespace WellSiteAutoPilot.Integration.PetroVisor;

/// <summary>
/// Read-only SDK compatibility probe. Discover actual available auth/session constructors
/// before implementing Windows SSO. Never instantiates a provider or performs network I/O.
/// </summary>
public static class PetroVisorSdkProbe
{
    public static PetroVisorSdkShape Inspect()
    {
        var client = Assembly.Load("MyrConn.PetroVisor.Web.Client");
        var api = Assembly.Load("MyrConn.PetroVisor.API");
        var provider = client.GetExportedTypes().Single(type => type.Name == "PetroVisorServiceProvider");
        var constructors = provider.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(ctor => string.Join(", ", ctor.GetParameters().Select(parameter =>
                parameter.ParameterType.FullName ?? parameter.ParameterType.Name)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        // Parameter *types* only: no URLs, usernames, credentials or customer metadata.
        return new PetroVisorSdkShape(
            api.GetName().Version?.ToString() ?? "unknown",
            client.GetName().Version?.ToString() ?? "unknown",
            constructors);
    }
}

public sealed record PetroVisorSdkShape(
    string ApiAssemblyVersion,
    string ClientAssemblyVersion,
    IReadOnlyList<string> ProviderConstructorParameterTypes);
