using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public sealed class ForeSiteReadPolicy : IAdapterReadPolicy
{
    public string AdapterKey => ForeSiteAdapter.Key;
    public string ResourceKind => ForeSiteAdapter.WellResourceKind;
    public bool IsSource(IReadOnlyDictionary<string, string?> settings, string externalId) =>
        !string.IsNullOrWhiteSpace(externalId);
    public bool CanRead(IReadOnlyDictionary<string, string?> settings, string externalId, string quantity,
        string access, string? capability, string? schemaId) => IsSource(settings, externalId) && access == "Current" &&
        (quantity is "PumpFillage" or "AveragePumpFillage" && capability is null && schemaId is null ||
         quantity == ForeSiteCards.Quantity && capability == "structured.current" && schemaId == "foresite.surface-cards/v1");
}
