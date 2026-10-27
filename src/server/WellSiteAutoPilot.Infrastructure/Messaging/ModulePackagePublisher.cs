using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Telemetry;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed partial class ModulePackagePublisher(WellSiteAutoPilotDbContext dbContext,
    IModulePackageObjectStore objectStore, TimeProvider clock, ILogger<ModulePackagePublisher> logger)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var ids = await dbContext.OutboxMessages.AsNoTracking()
            .Where(item => item.Type == ModulePackagePublication.OutboxType && item.ProcessedAtUtc == null &&
                (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
            .OrderBy(item => item.NextAttemptAtUtc ?? item.CreatedAtUtc).ThenBy(item => item.Id).Take(20)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var completed = 0;
        foreach (var id in ids)
        {
            try { if (await DispatchAsync(id, cancellationToken)) completed++; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // A failed artifact must not block another artifact in this batch,
                // and the separate execution dispatcher never selects these rows.
                LogPublicationFailure(logger, exception, id);
                dbContext.ChangeTracker.Clear();
                var attempts = await dbContext.OutboxMessages.AsNoTracking().Where(item => item.Id == id)
                    .Select(item => item.AttemptCount).SingleAsync(cancellationToken);
                var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts, 6))));
                var nextAttempt = clock.GetUtcNow() + delay;
                var code = exception is WellSiteAutoPilotException failure && failure.Code.Length <= 128
                    ? failure.Code : "MODULE_PACKAGE_PUBLICATION_FAILED";
                await dbContext.OutboxMessages.Where(item => item.Id == id && item.ProcessedAtUtc == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                        .SetProperty(item => item.NextAttemptAtUtc, nextAttempt)
                        .SetProperty(item => item.LastFailureCode, code), cancellationToken);
            }
        }
        return completed;
    }

    public async Task<bool> DispatchAsync(Guid publicationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var queued = await dbContext.OutboxMessages.FromSqlInterpolated($"""
            SELECT o.* FROM messaging.outbox_messages AS o
            WHERE o."Id" = {publicationId} AND o."Type" = {ModulePackagePublication.OutboxType}
              AND o."ProcessedAtUtc" IS NULL FOR UPDATE OF o SKIP LOCKED
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (queued.Length == 0) { await transaction.CommitAsync(cancellationToken); return false; }
        var request = JsonSerializer.Deserialize<ModulePackagePublication>(queued[0].Payload);
        if (request is null || request.LogicModuleId == Guid.Empty)
            throw InvalidPublication();
        using var activity = WellSiteTelemetry.StartConsumer("module package publish", request.TraceParent);
        activity?.SetTag(WellSiteTelemetry.CorrelationTag, request.CorrelationId);
        var modules = await dbContext.LogicModules.FromSqlInterpolated($"""
            SELECT m.* FROM logic.logic_modules AS m WHERE m."Id" = {request.LogicModuleId} FOR SHARE OF m
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (modules.Length != 1 || modules[0].ModuleId != request.ModuleId || modules[0].Version != request.ModuleVersion ||
            modules[0].PackageSha256 != request.PackageSha256)
            throw InvalidPublication();
        var current = modules[0];
        var approved = current.IsEnabled && current.TrustStatus is nameof(LogicModuleTrustStatus.CustomerApproved) or nameof(LogicModuleTrustStatus.TrustedPublisher);
        if (approved)
        {
            var package = await dbContext.LogicModulePackages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Sha256 == request.PackageSha256, cancellationToken)
                ?? throw new WellSiteAutoPilotException("MODULE_PACKAGE_NOT_AVAILABLE", FailureKind.DependencyUnavailable,
                    "The approved package is not available for publication.");
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(package.Content)), request.PackageSha256, StringComparison.Ordinal))
                throw new WellSiteAutoPilotException("MODULE_PACKAGE_HASH_MISMATCH", FailureKind.Validation,
                    "Stored package bytes do not match the approved checksum.");
            using var archive = new MemoryStream(package.Content);
            var inspected = await LogicModulePackageReader.ReadAsync(archive, cancellationToken);
            LogicModulePackageCompatibility.ValidateCatalog(inspected, current.ModuleId, current.Version, current.PackageSha256, current.ManifestJson);
            activity?.SetTag("module.id", current.ModuleId);
            using var publicationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            publicationTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await objectStore.PutAsync(request.PackageSha256, package.Content, publicationTimeout.Token);
        }
        // The module row stays locked through publication and commit. Retirement
        // either commits first and skips publication, or waits for publication.
        await dbContext.OutboxMessages.Where(item => item.Id == publicationId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ProcessedAtUtc, clock.GetUtcNow())
                .SetProperty(item => item.LastFailureCode, (string?)null).SetProperty(item => item.NextAttemptAtUtc, (DateTimeOffset?)null), cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = clock.GetUtcNow(), ActorIdentity = "system:package-publisher",
            CorrelationId = request.CorrelationId, Action = approved ? "logic-module.package-published" : "logic-module.package-publication-skipped",
            TargetType = "logic-module", TargetId = current.Id.ToString("D"),
            DetailsJson = JsonSerializer.Serialize(new { current.ModuleId, current.Version, current.PackageSha256, PublicationId = publicationId })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static WellSiteAutoPilotException InvalidPublication() => new("MODULE_PACKAGE_PUBLICATION_INVALID", FailureKind.Validation,
        "The publication request does not match the installed module.");

    [LoggerMessage(EventId = 2120, Level = LogLevel.Warning,
        Message = "Module package publication {PublicationId} failed and remains queued for retry.")]
    private static partial void LogPublicationFailure(ILogger logger, Exception exception, Guid publicationId);
}
