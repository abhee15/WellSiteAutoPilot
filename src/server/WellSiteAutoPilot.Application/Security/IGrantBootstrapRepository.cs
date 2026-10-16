namespace WellSiteAutoPilot.Application.Security;

public interface IGrantBootstrapRepository
{
    // The host must first verify the authenticated identity against its explicit
    // bootstrap allowlist. This is initialization, never a recovery backdoor.
    Task<bool> TryInitializeAdministratorAsync(Guid userId, DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}
