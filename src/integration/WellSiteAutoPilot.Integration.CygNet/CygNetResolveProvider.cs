using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// Decides whether CygNet can supply an input on a facility, from the connection's point map and the points the
/// facility really has. The decision is a pure function of those two lists so it can be tested without CygNet.
/// </summary>
public static class CygNetResolver
{
    public static ResolveResult Resolve(
        string input,
        IReadOnlyDictionary<string, CygNetQuantityMapping> quantities,
        IReadOnlyCollection<string> facilityUdcs)
    {
        if (!quantities.TryGetValue(input, out var mapping))
        {
            var known = string.Join(", ", quantities.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            return ResolveResult.Failed(ResolveOutcome.Misconfigured,
                $"'{input}' is not mapped for this CygNet connection. Mapped inputs: {known}.");
        }

        if (!facilityUdcs.Contains(mapping.Udc, StringComparer.OrdinalIgnoreCase))
            return ResolveResult.Failed(ResolveOutcome.NotApplicable,
                $"This facility has no point '{mapping.Udc}' that '{input}' maps to.");

        return ResolveResult.Compatible(new ResolveCandidate(
            $"cygnet:{mapping.Udc}", ResolveBasis.Current, $"{mapping.Udc} ({mapping.Unit})", mapping.Udc));
    }
}

/// <summary>Catalog and Resolve over a live CygNet facility. Catalog reads CygNet's point service; Resolve uses it.</summary>
public sealed class CygNetResolveProvider(CygNetProviderOptions options) : IAssetCatalogProvider, IResolveProvider
{
    public async ValueTask<IReadOnlyList<AssetCatalogEntry>> GetCatalogAsync(
        string assetExternalId, CancellationToken cancellationToken = default)
    {
        var domain = options.Domain;
        var udcs = await CygNetCallGate.Shared.RunAsync(
            $"{domain}/{assetExternalId[..assetExternalId.IndexOf("::", StringComparison.Ordinal)]}",
            () =>
            {
                var runtime = CygNetRuntime.Require();
                return runtime.PointUdcs(domain ?? runtime.AmbientDomain(), assetExternalId);
            },
            TimeSpan.FromSeconds(30), cancellationToken);
        return udcs.Select(u => new AssetCatalogEntry(u, null, null)).ToList();
    }

    public async ValueTask<ResolveResult> ResolveAsync(
        string assetExternalId, string input, ResolveBasis? basis = null, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(assetExternalId, cancellationToken);
        var result = CygNetResolver.Resolve(input, options.Quantities, catalog.Select(c => c.Name).ToList());
        return basis is null || result.Candidates.Any(c => c.Basis == basis) ? result
            : ResolveResult.Failed(ResolveOutcome.NotApplicable, $"CygNet offers '{input}' only as a current value, not '{basis}'.");
    }

    public async ValueTask<EngineeringValue> ReadAsync(
        string assetExternalId, ResolveCandidate candidate, CancellationToken cancellationToken = default) =>
        await new CygNetCurrentDataProvider(options).GetCurrentAsync(
            assetExternalId,
            options.Quantities.First(q => string.Equals(q.Value.Udc, candidate.ReadPlan, StringComparison.OrdinalIgnoreCase)).Key,
            cancellationToken);
}
