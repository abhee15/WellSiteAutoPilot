using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>No directory is implied by an authentication provider.</summary>
public sealed class UnavailableGroupDirectory : IGroupDirectory
{
    public string DirectoryId => "none";
    public bool SupportsGroupSearch => false;
    public Task<DirectoryGroup?> FindAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult<DirectoryGroup?>(null);
    public Task<GroupDirectoryPage> SearchAsync(string namePrefix, CancellationToken cancellationToken) =>
        throw new WellSiteAutoPilotException("SECURITY_DIRECTORY_UNSUPPORTED", FailureKind.Validation,
            "Group search is not available for the configured directory.");
}
