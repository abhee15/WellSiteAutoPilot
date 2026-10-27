using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed partial class LogicModuleCatalogRepository
{
    public async Task<ModulePackageState> ReadPackageStateAsync(Guid moduleId, string sha256, CancellationToken cancellationToken = default)
    {
        var stored = await dbContext.LogicModulePackages.AsNoTracking().AnyAsync(p => p.Sha256 == sha256, cancellationToken);
        var published = await ModulePackagePublicationQueries.IsPublishedAsync(dbContext, moduleId, cancellationToken);
        var queue = await dbContext.OutboxMessages.AsNoTracking().Where(q => q.Id == moduleId && q.Type == ModulePackagePublication.OutboxType)
            .Select(q => new { q.ProcessedAtUtc, q.AttemptCount, q.LastFailureCode, q.NextAttemptAtUtc }).SingleOrDefaultAsync(cancellationToken);
        var code = queue?.LastFailureCode;
        if (code is not { Length: > 0 and <= 128 } || !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')) code = null;
        return new(stored, published, queue is { ProcessedAtUtc: null }, queue?.AttemptCount ?? 0, code, queue?.NextAttemptAtUtc);
    }
}

internal static class ModulePackagePublicationQueries
{
    public static Task<bool> IsPublishedAsync(WellSiteAutoPilotDbContext db, Guid moduleId, CancellationToken cancellationToken)
    {
        var id = moduleId.ToString("D");
        return db.AuditEvents.AsNoTracking().AnyAsync(a => a.Action == "logic-module.package-published" &&
            a.TargetType == "logic-module" && a.TargetId == id, cancellationToken);
    }
}
