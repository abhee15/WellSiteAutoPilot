namespace WellSiteAutoPilot.Application.Security;

/// <summary>Directory metadata only. Results never establish membership or grant authority.</summary>
public interface IGroupDirectory
{
    string DirectoryId { get; }
    bool SupportsGroupSearch { get; }
    Task<DirectoryGroup?> FindAsync(string externalId, CancellationToken cancellationToken);
    Task<GroupDirectoryPage> SearchAsync(string namePrefix, CancellationToken cancellationToken);
}

/// <summary>Stable identity is the pair (DirectoryId, ExternalId), never DisplayName.</summary>
public sealed record DirectoryGroup(string DirectoryId, string ExternalId, string DisplayName, string Domain);
public sealed record GroupDirectoryPage(IReadOnlyCollection<DirectoryGroup> Groups, bool HasMore);
