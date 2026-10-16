using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class GrantConcurrencyFailureChecks
{
    // Deterministically cover provider wrapper shapes; the real two-writer
    // checks separately verify PostgreSQL's uniqueness/serialization behavior.
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessGrantSubject subject, CreateAccessGrantCommand command,
        SecurityActorContext actor, DateTimeOffset nowUtc)
    {
        foreach (var code in new[] { "40001", "40P01", "23505", "22001" })
        foreach (var depth in new[] { 0, 1, 2 })
        {
            Exception failure = new PostgresException("Injected provider failure", "ERROR", "ERROR", code);
            if (depth >= 1) failure = new DbUpdateException("Save failed", failure);
            if (depth == 2) failure = new InvalidOperationException("Transient provider failure", failure);
            var intercepted = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
                .AddInterceptors(new RejectGrantSave(failure)).Options;
            await using var db = new WellSiteAutoPilotDbContext(intercepted);
            try
            {
                await new AccessGrantRepository(db).CreateAsync(subject, command, actor, nowUtc);
                throw new InvalidOperationException("Injected grant save unexpectedly committed.");
            }
            catch (WellSiteAutoPilotException exception) when (code != "22001" &&
                exception.Code == "SECURITY_GRANT_CONFLICT" && ReferenceEquals(exception.InnerException, failure)) { }
            catch (Exception exception) when (code == "22001" && ReferenceEquals(exception, failure)) { }
        }
        await using var verification = new WellSiteAutoPilotDbContext(options);
        if (await verification.AccessGrants.AnyAsync(x => x.GroupSid == subject.GroupSid))
            throw new InvalidOperationException("Injected failed save left a group assignment.");
        Console.WriteLine("Grant writes normalize direct/nested PostgreSQL concurrency errors and preserve unrelated failures.");
    }

    private sealed class RejectGrantSave(Exception failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw failure;
    }
}
