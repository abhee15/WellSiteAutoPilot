using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>Uses current WSA authority around an administrative directory lookup.</summary>
public sealed class GroupDirectorySearchService(IGroupDirectory directory, IRequestAccessRepository access)
{
    public async Task<GroupDirectoryPage> SearchAuthorizedAsync(AccessSubject administrator,
        string namePrefix, CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(administrator, cancellationToken);
        var prefix = ValidatePrefix(namePrefix);
        if (!directory.SupportsGroupSearch)
            throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_UNSUPPORTED", FailureKind.Validation,
                "Group search is not available for the configured directory.");
        var page = await directory.SearchAsync(prefix, cancellationToken);
        // A directory lookup may be slow. Recheck WSA authority before disclosing results.
        await RequireAdministratorAsync(administrator, cancellationToken);
        return page;
    }

    public async Task<AccessGrantSubject> ResolveAuthorizedAsync(AccessSubject administrator,
        string directoryId, string externalId, CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(administrator, cancellationToken);
        if (directory.DirectoryId != directoryId || !directory.SupportsGroupSearch)
            throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_UNSUPPORTED", FailureKind.Validation,
                "The selected directory is not configured for group lookup.");
        var group = await directory.FindAsync(externalId, cancellationToken);
        await RequireAdministratorAsync(administrator, cancellationToken);
        if (group is null || group.DirectoryId != directoryId || group.ExternalId != externalId ||
            group.DisplayName.Length is < 1 or > 256 || group.Domain.Length > 256 ||
            group.DisplayName.Any(char.IsControl) || group.Domain.Any(char.IsControl))
            throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_GROUP_NOT_FOUND", FailureKind.NotFound,
                "The selected security group was not found. Search again.");
        return AccessGrantSubject.ForGroup(group.DirectoryId, group.ExternalId, group.DisplayName, group.Domain);
    }

    public async Task<(string DirectoryId, bool SupportsGroupSearch)> GetCapabilitiesAuthorizedAsync(
        AccessSubject administrator, CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(administrator, cancellationToken);
        return (directory.DirectoryId, directory.SupportsGroupSearch);
    }

    public static string ValidatePrefix(string? value)
    {
        var prefix = value?.Trim();
        if (prefix is null || prefix.Length is < 2 or > 100 ||
            prefix.Any(c => char.IsControl(c) || "*()\\".Contains(c)))
            throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_QUERY_INVALID", FailureKind.Validation,
                "Enter a group name prefix of 2 to 100 characters without wildcards or a domain prefix.");
        return prefix;
    }

    private async Task RequireAdministratorAsync(AccessSubject subject, CancellationToken cancellationToken)
    {
        if (!await access.HasAdministratorAsync(subject, cancellationToken))
            throw new WellSiteAutoPilotException("SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "A current protected administrator assignment is required.");
    }
}
