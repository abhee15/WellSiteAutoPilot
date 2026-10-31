namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Adapter-owned source and input rules. Shared persistence retains the authority checks.</summary>
public interface IAdapterReadPolicy
{
    string AdapterKey { get; }
    string ResourceKind { get; }
    bool IsSource(IReadOnlyDictionary<string, string?> settings, string externalId);
    bool CanRead(IReadOnlyDictionary<string, string?> settings, string externalId, string quantity,
        string access, string? capability, string? schemaId);
}
