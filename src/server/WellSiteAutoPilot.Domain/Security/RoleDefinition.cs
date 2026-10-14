namespace WellSiteAutoPilot.Domain.Security;

/// <summary>
/// Versioned role definition. Roles describe capabilities, while each later
/// role assignment independently specifies where the capabilities apply.
/// </summary>
public sealed record RoleDefinition(
    Guid Id,
    string Key,
    string Name,
    string Description,
    bool IsSystem,
    bool IsActive,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyCollection<string> Permissions);
