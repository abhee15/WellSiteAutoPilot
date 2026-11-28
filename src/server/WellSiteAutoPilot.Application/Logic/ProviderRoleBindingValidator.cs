using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>A configured logic's choice of connection for one provider role.</summary>
public sealed record ProviderRoleBinding(string Role, Guid ConnectionId);

/// <summary>What the binding check needs to know about a connection.</summary>
public sealed record ProviderConnectionInfo(Guid Id, string AdapterKey, bool Enabled);

/// <summary>
/// Checks a configured logic's role bindings against its module's version-3 provider dependencies. Pure: it neither
/// reads nor stores anything, so persistence and the executor can adopt it in later steps. Modules without
/// provider dependencies (versions 1 and 2) have no roles and keep using RequiredAdapterKey.
/// </summary>
public static class ProviderRoleBindingValidator
{
    public static void Validate(
        LogicModuleManifest manifest,
        IReadOnlyCollection<ProviderRoleBinding> bindings,
        Func<Guid, ProviderConnectionInfo?> findConnection)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(findConnection);

        var dependencies = manifest.ProviderDependencies ?? [];
        if (dependencies.Count == 0)
        {
            if (bindings.Count > 0) Fail("PROVIDER_ROLE_UNKNOWN", "This module declares no provider roles to bind.");
            return;
        }

        var byRole = new Dictionary<string, ProviderRoleBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            if (!dependencies.Any(d => string.Equals(d.Role, binding.Role, StringComparison.OrdinalIgnoreCase)))
                Fail("PROVIDER_ROLE_UNKNOWN", $"The module has no provider role '{binding.Role}'.");
            if (!byRole.TryAdd(binding.Role, binding))
                Fail("PROVIDER_ROLE_DUPLICATE", $"Provider role '{binding.Role}' is bound more than once.");
        }

        foreach (var dependency in dependencies)
        {
            if (!byRole.TryGetValue(dependency.Role, out var binding))
                Fail("PROVIDER_ROLE_UNBOUND", $"Provider role '{dependency.Role}' needs a connection.");
            var connection = findConnection(binding!.ConnectionId);
            if (connection is null)
                Fail("PROVIDER_CONNECTION_NOT_FOUND", $"The connection for role '{dependency.Role}' does not exist.");
            if (!connection!.Enabled)
                Fail("PROVIDER_CONNECTION_DISABLED", $"The connection for role '{dependency.Role}' is disabled.");
            if (!string.Equals(connection.AdapterKey, dependency.AdapterKey, StringComparison.OrdinalIgnoreCase))
                Fail("PROVIDER_ADAPTER_MISMATCH", $"Role '{dependency.Role}' needs adapter '{dependency.AdapterKey}', not '{connection.AdapterKey}'.");
        }
    }

    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(code, FailureKind.Validation, detail);
}
