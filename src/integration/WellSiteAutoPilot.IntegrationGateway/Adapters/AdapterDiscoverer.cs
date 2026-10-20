using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Lists what an adapter's system holds. One branch per adapter that can discover, next to its descriptor.</summary>
public sealed class AdapterDiscoverer(IHttpClientFactory clients)
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
        if (descriptor.Key != ForeSiteAdapter.Key)
            return new(false, [], ["This adapter cannot list what it holds yet."], actingAs);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var client = clients.CreateClient(AdapterConnectionTester.WindowsClientName);
        client.BaseAddress = ForeSiteAdapter.BaseAddress(settings[ForeSiteAdapter.BaseUrlSetting]!);
        client.Timeout = Timeout + TimeSpan.FromSeconds(5);
        return await ForeSiteAdapter.DiscoverAsync(client, actingAs, timeout.Token);
    }
}
