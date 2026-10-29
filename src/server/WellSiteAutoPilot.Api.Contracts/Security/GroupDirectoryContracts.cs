namespace WellSiteAutoPilot.Api.Contracts.Security;

/// <summary>Administrative metadata; never proof of membership or authorization.</summary>
public sealed record DirectoryGroupResponse(string DirectoryId, string ExternalId, string DisplayName, string Domain);
public sealed record GroupDirectorySearchResponse(IReadOnlyCollection<DirectoryGroupResponse> Groups, bool HasMore);
