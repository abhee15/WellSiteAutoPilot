namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record MaterializeLegacyGrantsRequest(bool Confirmed);

/// <summary>
/// Successful audit-only grant materialization. Runtime authorization
/// deliberately remains legacy until an independently verified cutover.
/// </summary>
public sealed record MaterializeLegacyGrantsResponse(
    int ActiveUsersInspected,
    int ActiveAssetsInspected,
    int GrantsCreated,
    int UsersAlreadyMatching,
    bool RecordedGrantsMatchLegacy,
    bool LiveAuthorizationUnchanged);

public sealed record RollbackLegacyGrantsRequest(bool Confirmed);

/// <summary>
/// Deactivation receipt for migration-origin entitlements only. Existing
/// production RBAC, historical audit events and customer grants are retained.
/// </summary>
public sealed record RollbackLegacyGrantsResponse(
    int MigratedGrantsInspected,
    int GrantsDeactivated,
    bool LiveAuthorizationUnchanged);
