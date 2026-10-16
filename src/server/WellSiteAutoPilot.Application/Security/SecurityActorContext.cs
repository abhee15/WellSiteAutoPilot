namespace WellSiteAutoPilot.Application.Security;

public sealed record SecurityActorContext(
    Guid? UserId,
    string IdentityName,
    string? CorrelationId)
{
    // Supplied by the trusted host, never deserialized from an API request.
    // TestHeader identities cannot contribute Windows group authority.
    public VerifiedWindowsGroupMembership? WindowsGroups { get; init; }
    public bool RequiresWindowsGroupProof { get; init; } = true;

    public static SecurityActorContext Bootstrap { get; } =
        new(null, "system:bootstrap", null);
}
