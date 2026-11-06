using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.Wami;

/// <summary>Creates a WAMI client for a router address. The real net.tcp client needs WAMI's own contract DLL, so a build opts in.</summary>
public interface IWamiClientFactory
{
    IWamiClient Create(Uri routerAddress);
}

/// <summary>The connection test for a WAMI connection: settings check, then a licence round trip.</summary>
public static class WamiConnectionCheck
{
    public static Uri? ReadEndpoint(IReadOnlyDictionary<string, string?> settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        problem = null;
        if (!settings.TryGetValue(WamiAdapter.EndpointSetting, out var text) || !Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var address) || address.Scheme != "net.tcp")
        {
            problem = "The WAMI address must be a net.tcp address such as net.tcp://wami-server:5656/Weatherford/IRouter.";
            return null;
        }

        return address;
    }

    public static async Task<AdapterTestResult> CheckAsync(IWamiClient client, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            var info = await client.GetServerInfoAsync(cancellationToken);
            var analyses = info.LicensedFeatures.Where(f => f != "WAMI").ToArray();
            return new(AdapterTestOutcomes.Healthy,
                analyses.Length > 0
                    ? $"WAMI answered and is licensed for: {string.Join(", ", analyses)}."
                    : "WAMI answered and holds a base licence, but no analysis feature is licensed.", actingAs);
        }
        catch (WamiException error) when (error.Code == WamiFailureCodes.Unavailable || error.Code == WamiFailureCodes.Timeout)
        {
            return new(AdapterTestOutcomes.Unreachable, "WAMI could not be reached at that address, or this account was refused.", actingAs);
        }
        catch (WamiException error) when (error.Code == WamiFailureCodes.Busy)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "WAMI is running but has no free worker right now.", actingAs);
        }
        catch (WamiException error) when (error.Code == WamiFailureCodes.LicenseMissing)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "WAMI answered but its licence is not valid.", actingAs);
        }
        catch (WamiException)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "WAMI answered with an error.", actingAs);
        }
    }
}
