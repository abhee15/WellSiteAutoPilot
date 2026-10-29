namespace WellSiteAutoPilot.Application.Security;

public sealed record SecurityActorContext(
    Guid? UserId,
    string IdentityName,
    string? CorrelationId)
{
    // Supplied by the trusted host, never deserialized from an API request.
    // TestHeader identities cannot contribute Windows group authority.
    public VerifiedGroupMembership? Groups { get; init; }
    public bool RequiresGroupProof { get; init; } = true;

    public static SecurityActorContext Bootstrap { get; } =
        new(null, "system:bootstrap", null);
}
