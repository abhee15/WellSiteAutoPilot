using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Audit;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

internal static partial class AuthorizedSecurityDirectoryChecks
{
    /// <summary>
    /// The audit read is newest first, keyset paged with a stable tie-break, filters in SQL, treats
    /// LIKE wildcards in the prefix as text, and rejects malformed input.
    /// </summary>
    private static async Task CheckAuditEventsAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options, AccessSubject administrator)
    {
        CheckAuditCursor();

        var prefix = $"zz-audit-{Guid.NewGuid():N}.";
        var actorA = Guid.NewGuid();
        var actorB = Guid.NewGuid();
        var assetA = Guid.NewGuid();
        // Far-future times keep these rows ahead of every other fixture event.
        var baseTime = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tieTime = baseTime.AddMinutes(10);
        var rows = new List<AuditEventEntity>
        {
            Row(baseTime.AddMinutes(1), "one", actorA, "Asset", assetA),
            Row(baseTime.AddMinutes(2), "two", actorB, "Asset", null),
            Row(tieTime, "tie-a", actorA, "LogicModule", null),
            Row(tieTime, "tie-b", actorA, "LogicModule", null),
            Row(baseTime.AddMinutes(20), "wild_card", actorB, "Asset", null),
            Row(baseTime.AddMinutes(21), "wildXcard", actorB, "Asset", null),
        };

        await using (var setup = new WellSiteAutoPilotDbContext(options))
        {
            setup.AuditEvents.AddRange(rows);
            await setup.SaveChangesAsync();
        }

        var rowIds = rows.Select(item => item.Id).ToArray();
        try
        {
            async Task<AuditEventPage> ReadAsync(AuditEventQuery query)
            {
                await using var db = new WellSiteAutoPilotDbContext(options);
                return await new SecurityDirectoryRepository(db, TimeProvider.System)
                    .ListAuditEventsAuthorizedAsync(administrator, query);
            }

            var expected = rows.OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id).Select(item => item.Id).ToArray();

            var all = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix));
            if (!all.Items.Select(item => item.Id).SequenceEqual(expected) || all.Next is not null)
                throw new InvalidOperationException("Audit events were not returned newest first with a stable tie-break.");

            // Walk the pages two at a time; every event appears exactly once, in order.
            var collected = new List<Guid>();
            AuditEventCursor? cursor = null;
            var pages = 0;
            do
            {
                var page = await ReadAsync(new AuditEventQuery(Limit: 2, After: cursor, ActionPrefix: prefix));
                collected.AddRange(page.Items.Select(item => item.Id));
                cursor = page.Next;
                pages++;
            }
            while (cursor is not null && pages < 10);

            if (!collected.SequenceEqual(expected) || pages != 3)
                throw new InvalidOperationException("Audit keyset paging skipped or repeated events.");

            var byActor = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix, ActorUserId: actorB));
            var byType = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix, TargetType: "LogicModule"));
            var byAsset = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix, AssetId: assetA));
            var byRange = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix,
                FromUtc: baseTime.AddMinutes(2), ToUtc: tieTime));
            if (byActor.Items.Count != 3 || byType.Items.Count != 2 || byAsset.Items.Count != 1 ||
                byRange.Items.Count != 3)
                throw new InvalidOperationException("Audit filters did not narrow the result as requested.");

            // "_" and "%" in a prefix are literal characters, not LIKE wildcards.
            var literal = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix + "wild_"));
            var percent = await ReadAsync(new AuditEventQuery(Limit: 200, ActionPrefix: prefix + "%"));
            if (literal.Items.Count != 1 || literal.Items.Single().Action != prefix + "wild_card" ||
                percent.Items.Count != 0)
                throw new InvalidOperationException("Audit action prefix treated LIKE wildcards as patterns.");

            var first = all.Items.First();
            if (first.DetailsJson != "{}" || first.ActorIdentity != "audit-test")
                throw new InvalidOperationException("Audit event fields were not returned intact.");

            foreach (var invalid in new[]
            {
                new AuditEventQuery(Limit: 0),
                new AuditEventQuery(Limit: AuditEventQuery.MaximumLimit + 1),
                new AuditEventQuery(ActionPrefix: string.Empty),
                new AuditEventQuery(ActionPrefix: "bad\u0007prefix"),
                new AuditEventQuery(TargetType: new string('x', AuditEventQuery.MaximumTextLength + 1)),
                new AuditEventQuery(FromUtc: baseTime.AddDays(1), ToUtc: baseTime),
            })
                await ExpectAsync(() => ReadAsync(invalid), FailureKind.Validation);
        }
        finally
        {
            await using var cleanup = new WellSiteAutoPilotDbContext(options);
            await cleanup.AuditEvents.Where(item => rowIds.Contains(item.Id)).ExecuteDeleteAsync();
        }

        AuditEventEntity Row(DateTimeOffset at, string name, Guid actor, string targetType, Guid? asset) => new()
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = at,
            ActorUserId = actor,
            ActorIdentity = "audit-test",
            Action = prefix + name,
            TargetType = targetType,
            TargetId = name,
            AssetId = asset,
            CorrelationId = null,
            DetailsJson = "{}",
        };
    }

    private static void CheckAuditCursor()
    {
        var cursor = new AuditEventCursor(new DateTimeOffset(2100, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1234), Guid.NewGuid());
        var token = cursor.Encode();
        if (token.Any(c => c is '+' or '/' or '=') ||
            !AuditEventCursor.TryDecode(token, out var decoded) || decoded != cursor)
            throw new InvalidOperationException("Audit cursor did not round-trip as a URL-safe token.");

        foreach (var bad in new[]
        {
            null, string.Empty, "###", "AAAA", new string('A', 200),
            Convert.ToBase64String("12:not-a-guid"u8.ToArray()),
            Convert.ToBase64String("-5:00000000000000000000000000000000"u8.ToArray()),
            Convert.ToBase64String("5"u8.ToArray()),
        })
            if (AuditEventCursor.TryDecode(bad, out _))
                throw new InvalidOperationException($"Audit cursor accepted a malformed token: {bad}.");
    }
}
