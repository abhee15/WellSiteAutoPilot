using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository
{
    // Called inside the execution/outbox transaction. Retirement either wins
    // first and refuses new work, or waits until the accepted snapshot commits.
    private async Task<ExecutionPackageV3?> PinModulePackageAsync(string moduleId, string version, CancellationToken cancellationToken)
    {
        var modules = await dbContext.LogicModules.FromSqlInterpolated($"""
            SELECT m.* FROM logic.logic_modules AS m
            WHERE m."ModuleId" = {moduleId} AND m."Version" = {version} FOR SHARE OF m
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var module = modules.SingleOrDefault();
        // Metadata-only and built-in registrations keep the previous request contract.
        if (module is null || !await dbContext.LogicModulePackages.AnyAsync(item => item.Sha256 == module.PackageSha256, cancellationToken))
            return null;
        if (!module.IsEnabled || module.TrustStatus is not (nameof(LogicModuleTrustStatus.CustomerApproved) or nameof(LogicModuleTrustStatus.TrustedPublisher)))
            throw new WellSiteAutoPilotException("MODULE_NOT_ENABLED", FailureKind.Conflict,
                "Only an approved enabled package can be dispatched.");
        if (module.Runtime != nameof(LogicRuntimeKind.DotNet))
            throw new WellSiteAutoPilotException("MODULE_RUNTIME_NOT_SUPPORTED", FailureKind.Validation,
                "This deployment currently hosts approved .NET packages only.");
        if (!await WellSiteAutoPilot.Persistence.Logic.ModulePackagePublicationQueries.IsPublishedAsync(dbContext, module.Id, cancellationToken))
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_NOT_PUBLISHED", FailureKind.Conflict,
                "The approved package has not completed delivery. Check publication status before retrying.");
        return new ExecutionPackageV3(module.PackageSha256, module.ManifestJson);
    }
}
