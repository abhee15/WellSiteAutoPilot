using System.Reflection;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

public sealed record CygNetQuantityMapping(string Udc, string Unit);
public sealed record CygNetProviderOptions(ushort? Domain, IReadOnlyDictionary<string, CygNetQuantityMapping> Quantities);

/// <summary>Development current-data route. Saved executions use the connection-backed route instead.</summary>
public sealed class CygNetCurrentDataProvider(CygNetProviderOptions options, CygNetRuntime runtime) : ICurrentDataProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity, CancellationToken cancellationToken = default)
    {
        if (!options.Quantities.TryGetValue(quantity, out var mapping)) throw Failure("CYGNET_QUANTITY_UNSUPPORTED");
        // No ambient-domain fallback: even the development route must name its domain explicitly.
        var parts = assetExternalId.Split("::", StringSplitOptions.None);
        var service = parts[0].Split('.', StringSplitOptions.None);
        if (options.Domain is not { } domain || parts.Length != 2 || service.Length != 2) throw Failure("CYGNET_CONNECTION_INVALID");
        var settings = new Dictionary<string, string?> { ["domain"] = domain.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["site"] = service[0], ["service"] = service[1] };
        var connection = CygNetAdapter.ReadConnection(settings, out _);
        if (connection is null || !connection.Contains(assetExternalId)) throw Failure("CYGNET_FACILITY_INVALID");
        var point = await runtime.ReadAsync(connection, assetExternalId, mapping.Udc, cancellationToken);
        return new(quantity, point.Value, mapping.Unit, point.TimestampUtc, point.Quality, CygNetAdapter.Key, DateTimeOffset.UtcNow);
    }
    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.Validation,
        "The CygNet development provider requires an explicit domain and configured numeric quantity.");
}

internal static class CygNetAssemblyResolver
{
    private static int _registered;
    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            // Resolve vendor mixed-mode assemblies only; do not intercept framework or unrelated dependencies.
            if (name is null || !name.EndsWith("64", StringComparison.Ordinal) ||
                !(name.StartsWith("CygNet", StringComparison.Ordinal) || name.StartsWith("Cx", StringComparison.Ordinal))) return null;
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
