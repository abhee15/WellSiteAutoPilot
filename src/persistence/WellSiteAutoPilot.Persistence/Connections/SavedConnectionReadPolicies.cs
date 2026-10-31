using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.Integration.ForeSite;

namespace WellSiteAutoPilot.Persistence.Connections;

// One composition point for adapter-owned pure source rules. No native API or Gateway access in persistence.
internal static class SavedConnectionReadPolicies
{
    private static readonly IAdapterReadPolicy[] Policies = [new ForeSiteReadPolicy(), new CygNetReadPolicy()];
    public static IAdapterReadPolicy? Find(string key) => Policies.FirstOrDefault(p => p.AdapterKey == key);
}
