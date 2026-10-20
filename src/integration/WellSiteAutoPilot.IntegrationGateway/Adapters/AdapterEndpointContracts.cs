using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

public sealed record ValidateAdapterSettingsRequest(Dictionary<string, string?>? Settings);

public sealed record ValidateAdapterSettingsResponse(IReadOnlyList<AdapterSettingProblem> Problems);
