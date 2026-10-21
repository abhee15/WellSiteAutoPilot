using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.PetroVisor;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Tries a connection's settings against the outside system. One branch per adapter, kept next to its descriptor.</summary>
public sealed class AdapterConnectionTester(IHttpClientFactory clients, IPetroVisorSessionFactory petroVisorSessions)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<AdapterTestResult> TestAsync(
        AdapterDescriptor descriptor, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(settings);
        var actingAs = ActingAs();
        if (AdapterSettingsValidator.Validate(descriptor, settings).Count > 0)
            return new(AdapterTestOutcomes.NeedsAttention, "The saved settings are no longer valid for this adapter.", actingAs);

        return descriptor.Key switch
        {
            "simulator" => await TestSimulatorAsync(settings, actingAs, cancellationToken),
            ForeSiteAdapter.Key => await TestForeSiteAsync(settings, actingAs, cancellationToken),
            PetroVisorAdapter.Key => await TestPetroVisorAsync(settings, cancellationToken),
            _ => new(AdapterTestOutcomes.NeedsAttention, "This adapter cannot be tested yet.", actingAs)
        };
    }

    private async Task<AdapterTestResult> TestSimulatorAsync(
        IReadOnlyDictionary<string, string?> settings, string actingAs, CancellationToken cancellationToken)
    {
        var address = new Uri(new Uri(settings["baseUrl"]!.Trim().TrimEnd('/') + "/"), "health/ready");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var client = clients.CreateClient("adapter-test");
            using var response = await client.GetAsync(address, timeout.Token);
            return response.IsSuccessStatusCode
                ? new(AdapterTestOutcomes.Healthy, "The simulator answered and reports it is ready.", actingAs)
                : new(AdapterTestOutcomes.NeedsAttention,
                    $"The simulator answered but is not ready (status {(int)response.StatusCode}).", actingAs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AdapterTestOutcomes.Unreachable, "The simulator did not answer in time.", actingAs);
        }
        catch (HttpRequestException)
        {
            return new(AdapterTestOutcomes.Unreachable, "The simulator could not be reached at that address.", actingAs);
        }
    }

    private async Task<AdapterTestResult> TestForeSiteAsync(
        IReadOnlyDictionary<string, string?> settings, string actingAs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        // The client presents the Gateway's own Windows identity, which is what ForeSite will see in use.
        using var client = clients.CreateClient(WindowsClientName);
        client.BaseAddress = ForeSiteAdapter.BaseAddress(settings[ForeSiteAdapter.BaseUrlSetting]!);
        return await ForeSiteAdapter.CheckAsync(client, actingAs, timeout.Token);
    }

    private async Task<AdapterTestResult> TestPetroVisorAsync(
        IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var reader = new PetroVisorSdkEntityReader(
            petroVisorSessions, PetroVisorAdapter.DiscoveryAddress(settings[PetroVisorAdapter.EndpointSetting]!), PetroVisorAdapter.IdentityAddress(settings));
        return await PetroVisorAdapter.CheckAsync(
            reader, settings[PetroVisorAdapter.WorkspaceSetting]!.Trim(), petroVisorSessions.Identity, timeout.Token);
    }

    public const string WindowsClientName = "adapter-test-windows";

    private static string ActingAs() => $"{Environment.UserDomainName}\\{Environment.UserName}";
}
