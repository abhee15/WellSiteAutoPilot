namespace WellSiteAutoPilot.Application.Logic;

public sealed record ModulePackageState(bool IsStored, bool PublicationRecorded, bool PublicationPending,
    int AttemptCount, string? LastFailureCode, DateTimeOffset? NextAttemptAtUtc);

public interface IModulePackageStateRepository
{
    Task<ModulePackageState> ReadPackageStateAsync(Guid moduleId, string sha256, CancellationToken cancellationToken = default);
}
