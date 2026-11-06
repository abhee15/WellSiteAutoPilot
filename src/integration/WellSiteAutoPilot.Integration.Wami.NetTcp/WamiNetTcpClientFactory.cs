namespace WellSiteAutoPilot.Integration.Wami.NetTcp;

/// <summary>Register this in a Gateway build that ships WAMI's contract DLL; without it the Gateway reports that WAMI cannot be tested.</summary>
public sealed class WamiNetTcpClientFactory : IWamiClientFactory
{
    public IWamiClient Create(Uri routerAddress) => new WamiNetTcpClient(routerAddress);
}
