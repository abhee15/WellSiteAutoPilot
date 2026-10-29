using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.PetroVisor;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Lists what an adapter's system holds. One branch per adapter that can discover, next to its descriptor.</summary>
public sealed class AdapterDiscoverer(
    IHttpClientFactory clients, IPetroVisorSessionFactory petroVisorSessions, ICygNetFacilityReader cygNet)
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
        if (descriptor.Key != ForeSiteAdapter.Key && descriptor.Key != PetroVisorAdapter.Key && descriptor.Key != CygNetAdapter.Key)
            return new(false, [], ["This adapter cannot list what it holds yet."], actingAs);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        if (descriptor.Key == CygNetAdapter.Key)
        {
            var connection = CygNetAdapter.ReadConnection(settings, out var problem);
            return connection is null
                ? new(false, [], [problem!], actingAs)
                : await CygNetAdapter.DiscoverAsync(cygNet, connection, actingAs, timeout.Token);
        }
        if (descriptor.Key == PetroVisorAdapter.Key)
        {
            var address = PetroVisorAdapter.DiscoveryAddress(settings[PetroVisorAdapter.EndpointSetting]!);
            if (PetroVisorAdapter.AddressProblem(address) is { } problem)
                return new(false, [], [problem], petroVisorSessions.Identity);
            var reader = new PetroVisorSdkEntityReader(petroVisorSessions, address, PetroVisorAdapter.IdentityAddress(settings));
            return await PetroVisorAdapter.DiscoverAsync(
                reader, settings[PetroVisorAdapter.WorkspaceSetting]!.Trim(), petroVisorSessions.Identity, timeout.Token);
        }

        using var client = clients.CreateClient(AdapterConnectionTester.WindowsClientName);
        client.BaseAddress = ForeSiteAdapter.BaseAddress(settings[ForeSiteAdapter.BaseUrlSetting]!);
        client.Timeout = Timeout + TimeSpan.FromSeconds(5);
        return await ForeSiteAdapter.DiscoverAsync(client, actingAs, timeout.Token);
    }
}
