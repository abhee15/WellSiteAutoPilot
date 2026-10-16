namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record ProposedAuthorizedAssetResponse(Guid Id, string Name);

public sealed record ProposedAssetAccessResponse(
    Guid UserId,
    string Permission,
    int Offset,
    int Limit,
    IReadOnlyCollection<ProposedAuthorizedAssetResponse> Assets,
    bool IsDiagnosticOnly);

