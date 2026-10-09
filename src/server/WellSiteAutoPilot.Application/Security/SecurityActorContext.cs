namespace WellSiteAutoPilot.Application.Security;

public sealed record SecurityActorContext(
    Guid? UserId,
    string IdentityName,
    string? CorrelationId)
{
    public static SecurityActorContext Bootstrap { get; } =
        new(null, "system:bootstrap", null);
}
