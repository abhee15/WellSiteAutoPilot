using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.PetroVisor;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Lists what an adapter's system holds. One branch per adapter that can discover, next to its descriptor.</summary>
public sealed class AdapterDiscoverer(IHttpClientFactory clients, IPetroVisorSessionFactory petroVisorSessions)
{
    /// <summary>ForeSite returns every well in one response and can be slow, so discovery gets more time than a test.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public async Task<DiscoveryResult> DiscoverAsync(
        AdapterDescriptor descriptor, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(settings);
        var actingAs = $"{Environment.UserDomainName}\\{Environment.UserName}";
        if (AdapterSettingsValidator.Validate(descriptor, settings).Count > 0)
            return new(false, [], ["The saved settings are no longer valid for this adapter."], actingAs);
        if (descriptor.Key != ForeSiteAdapter.Key && descriptor.Key != PetroVisorAdapter.Key)
            return new(false, [], ["This adapter cannot list what it holds yet."], actingAs);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        if (descriptor.Key == PetroVisorAdapter.Key)
        {
            var reader = new PetroVisorSdkEntityReader(
                petroVisorSessions, PetroVisorAdapter.DiscoveryAddress(settings[PetroVisorAdapter.EndpointSetting]!), PetroVisorAdapter.IdentityAddress(settings));
            return await PetroVisorAdapter.DiscoverAsync(
                reader, settings[PetroVisorAdapter.WorkspaceSetting]!.Trim(), petroVisorSessions.Identity, timeout.Token);
        }

        using var client = clients.CreateClient(AdapterConnectionTester.WindowsClientName);
        client.BaseAddress = ForeSiteAdapter.BaseAddress(settings[ForeSiteAdapter.BaseUrlSetting]!);
        client.Timeout = Timeout + TimeSpan.FromSeconds(5);
        return await ForeSiteAdapter.DiscoverAsync(client, actingAs, timeout.Token);
    }
}
