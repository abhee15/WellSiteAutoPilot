namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record RolePermissionDefinitionResponse(
    string Key, string Category, string ScopeKind, bool IsPrivileged);

public sealed record RoleDefinitionResponse(
    Guid RoleId,
    string Key,
    string Name,
    string Description,
    bool IsSystem,
    bool IsActive,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyCollection<string> Permissions);

public sealed record CreateSecurityRoleRequest(
    string Name,
    string Description,
    IReadOnlyCollection<string> Permissions);

public sealed record UpdateSecurityRoleRequest(
    long ExpectedRevision,
    string Name,
    string Description,
    bool IsActive,
    IReadOnlyCollection<string> Permissions);
