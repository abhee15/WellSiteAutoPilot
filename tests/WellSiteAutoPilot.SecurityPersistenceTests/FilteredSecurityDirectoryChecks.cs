using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static partial class AuthorizedSecurityDirectoryChecks
{
    private static async Task CheckFilteredDirectoryAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options, AccessSubject administrator)
    {
        var now = DateTimeOffset.UtcNow;
        var roleId = Guid.NewGuid();
        var ids = new List<Guid>();
        await using var setup = new WellSiteAutoPilotDbContext(options);
        setup.RoleDefinitions.Add(new RoleDefinitionEntity
        {
            Id = roleId, Key = roleId.ToString("N"), Name = roleId.ToString("N"),
            NormalizedName = roleId.ToString("N").ToUpperInvariant(), IsActive = true,
            Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        for (var i = 0; i < 125; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            var name = $"DIRECTORY-PROBE-{i:D3}";
            setup.Users.Add(new UserEntity
            {
                Id = id, IdentityKey = $"user-directory-probe:{id:N}",
                IdentityName = name, NormalizedIdentityName = name,
                DisplayName = i == 124 ? "Unique Searchable Person" : null,
                IsActive = i != 2,
                CreatedAtUtc = now.AddMinutes(-i), LastSeenAtUtc = now.AddMinutes(-i),
            });
        }
        setup.AccessGrants.Add(new AccessGrantEntity
        {
            Id = Guid.NewGuid(), SubjectKind = "User", UserId = ids[0], RoleId = roleId,
            ScopeKind = "DeploymentWide", IsActive = true, Revision = 1,
            CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await setup.SaveChangesAsync();
        setup.ChangeTracker.Clear();

        try
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var directory = new SecurityDirectoryRepository(db, TimeProvider.System);
            var filtered = new SecurityUserDirectoryQuery(Limit: 20, Search: "directory-probe-");
            var seen = new List<Guid>();
            SecurityUserDirectoryCursor? cursor = null;
            for (var pageNo = 0; pageNo < 10; pageNo++)
            {
                var page = await directory.SearchUsersAuthorizedAsync(administrator, filtered with { After = cursor });
                if (page.Items.Count is < 1 or > 20)
                    throw new InvalidOperationException("The user directory returned an incorrect page length.");
                seen.AddRange(page.Items.Select(item => item.User.Id));
                cursor = page.Next;
                if (cursor is null) break;
                if (!SecurityUserDirectoryCursor.TryDecode(cursor.Encode(), out var roundtrip) || roundtrip != cursor)
                    throw new InvalidOperationException("User directory cursor did not round trip.");
            }
            if (seen.Count != 125 || seen.Distinct().Count() != 125 || !seen.SequenceEqual(ids))
                throw new InvalidOperationException("User search/pagination lost accounts beyond the first page.");

            var noDirect = await directory.SearchUsersAuthorizedAsync(administrator,
                new SecurityUserDirectoryQuery(Search: "directory-probe-", AccountStatus: "active",
                    DirectAssignments: "without"));
            if (noDirect.Items.Count != 50 || noDirect.Items.Any(entry =>
                !entry.User.IsActive || entry.ActiveDirectGrantCount != 0 ||
                entry.User.Id == ids[0] || entry.User.Id == ids[2]))
                throw new InvalidOperationException("Direct-assignment/status filters were not applied before page limits.");

            var direct = await directory.SearchUsersAuthorizedAsync(administrator,
                new SecurityUserDirectoryQuery(Search: "directory-probe-", DirectAssignments: "with"));
            if (direct.Items.Count != 1 || direct.Items[0].User.Id != ids[0])
                throw new InvalidOperationException("Direct-assignment filter included users without usable direct grants.");

            var inactive = await directory.SearchUsersAuthorizedAsync(administrator,
                new SecurityUserDirectoryQuery(Search: "directory-probe-", AccountStatus: "inactive"));
            if (inactive.Items.Count != 1 || inactive.Items[0].User.Id != ids[2])
                throw new InvalidOperationException("Inactive account filter did not preserve independent status.");

            var named = await directory.SearchUsersAuthorizedAsync(administrator,
                new SecurityUserDirectoryQuery(Search: "unique searchable person"));
            if (named.Items.Count != 1 || named.Items[0].User.Id != ids[124])
                throw new InvalidOperationException("Search did not match case-insensitive display names beyond page limits.");
            var literal = await directory.SearchUsersAuthorizedAsync(administrator,
                new SecurityUserDirectoryQuery(Search: "%"));
            if (literal.Items.Any(entry => ids.Contains(entry.User.Id)))
                throw new InvalidOperationException("Search treated LIKE wildcards as user patterns.");

            foreach (var query in new[]
            {
                new SecurityUserDirectoryQuery(Limit: 0),
                new SecurityUserDirectoryQuery(AccountStatus: "unknown"),
                new SecurityUserDirectoryQuery(DirectAssignments: "waiting"),
                new SecurityUserDirectoryQuery(Search: " x "),
                new SecurityUserDirectoryQuery(Search: new string('x', 101)),
            })
                await ExpectAsync(() => directory.SearchUsersAuthorizedAsync(administrator, query), FailureKind.Validation);
            await ExpectAsync(() => directory.SearchUsersAuthorizedAsync(new AccessSubject(Guid.NewGuid()),
                new SecurityUserDirectoryQuery()), FailureKind.Authorization);
            if (SecurityUserDirectoryCursor.TryDecode("invalid", out _) ||
                SecurityUserDirectoryCursor.TryDecode("!bad", out _))
                throw new InvalidOperationException("An invalid user directory cursor was accepted.");
        }
        finally
        {
            await setup.AccessGrants.Where(grant => grant.UserId != null && ids.Contains(grant.UserId.Value))
                .ExecuteDeleteAsync();
            await setup.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
            await setup.RoleDefinitions.Where(role => role.Id == roleId).ExecuteDeleteAsync();
        }
    }
}
