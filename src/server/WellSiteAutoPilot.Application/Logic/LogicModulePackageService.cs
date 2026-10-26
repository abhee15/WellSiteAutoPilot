using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public sealed class LogicModulePackageService(ILogicModulePackageRepository repository, TimeProvider clock)
{
    // The first server upload slice is deliberately smaller than offline inspection.
    public const int MaximumUploadBytes = 16 * 1024 * 1024;

    public async Task<InstalledLogicModule> UploadAsync(Stream source, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        using var candidate = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (candidate.Length + read > MaximumUploadBytes)
                throw new WellSiteAutoPilotException("MODULE_PACKAGE_UPLOAD_TOO_LARGE", FailureKind.Validation,
                    "Module package uploads must not exceed 16 MiB.");
            await candidate.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        candidate.Position = 0;
        var inspected = await LogicModulePackageReader.ReadAsync(candidate, cancellationToken);
        var manifest = inspected.Manifest;
        var module = new InstalledLogicModule(Guid.NewGuid(), manifest.ModuleId, manifest.Version,
            manifest.DisplayName, manifest.Publisher, manifest.Runtime, manifest.ExecutionProfile,
            inspected.ManifestJson, inspected.Sha256, LogicModuleTrustStatus.Untrusted, false, clock.GetUtcNow());
        return await repository.UploadAuthorizedAsync(module, candidate.ToArray(), subject, actor, cancellationToken);
    }
}
