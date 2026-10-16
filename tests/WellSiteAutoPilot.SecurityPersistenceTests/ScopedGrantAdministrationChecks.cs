using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class ScopedGrantAdministrationChecks
{
    public static async Task RunAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile operatorUser, UserAccessProfile administrator,
        UserAccessProfile secondAdministrator, Guid assetId, Guid otherAssetId,
        TimeProvider clock)
    {
        await using var dbContext = new WellSiteAutoPilotDbContext(options);
        var repository = new UserAccessRepository(dbContext);
        var service = new UserAccessService(repository, clock);
        var roleCatalog = new RoleCatalogService(
            new RoleCatalogRepository(dbContext), repository, clock);
        var actor = new SecurityActorContext(
            secondAdministrator.Id, secondAdministrator.IdentityName, "grant-test");
        var grantCatalogRole = await roleCatalog.CreateAsync(
            new RoleMutationCommand("Scoped Assignment Role", "Grant regression role",
                [WellSitePermissions.AssetsRead], true), actor);

        // The first persisted grants are additive and audit-only. Existing user's
        // compiled roles and Asset scope remain unchanged until evaluator cutover.
        var accessGrantService = new AccessGrantAdministrationService(
            new AccessGrantRepository(dbContext),
            repository,
            new RoleCatalogRepository(dbContext),
            clock);
        var assignmentActor = new SecurityActorContext(
            secondAdministrator.Id, secondAdministrator.IdentityName, "grant-test");

        // The administrator reviewed revision one; a separate role edit
        // wins before creation begins. Reject both service and direct repository
        // writes against that stale review without recording a grant or audit.
        var reviewedRevision = grantCatalogRole.Revision;
        grantCatalogRole = await roleCatalog.UpdateAsync(
            grantCatalogRole.Id, reviewedRevision,
            new RoleMutationCommand(grantCatalogRole.Name, "Changed after review",
                [WellSitePermissions.AssetsRead, WellSitePermissions.AssetsManage], true), actor);
        var staleCommand = new CreateAccessGrantCommand(grantCatalogRole.Id,
            AccessGrantScopeKind.ExactAssets, [assetId], reviewedRevision);
        var grantsBeforeStale = await dbContext.AccessGrants.CountAsync();
        var auditsBeforeStale = await dbContext.AuditEvents.CountAsync();
        try
        {
            await accessGrantService.CreateForUserAsync(operatorUser.Id, staleCommand, actor);
            throw new InvalidOperationException("Stale role review was accepted by the service.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_GRANT_ROLE_CONFLICT")
        {
        }
        await using (var staleWriter = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await new AccessGrantRepository(staleWriter).CreateForUserAsync(
                    operatorUser.Id, staleCommand, actor, clock.GetUtcNow());
                throw new InvalidOperationException("Transactional role-revision fence was bypassed.");
            }
            catch (WellSiteAutoPilotException exception)
                when (exception.Code == "SECURITY_GRANT_ROLE_CONFLICT")
            {
            }
        }
        if (await dbContext.AccessGrants.CountAsync() != grantsBeforeStale ||
            await dbContext.AuditEvents.CountAsync() != auditsBeforeStale)
        {
            throw new InvalidOperationException("Stale role review left grant or audit writes.");
        }

        // An audit insert failure must abort the grant and its scope rows.
        var grantsBefore = await dbContext.AccessGrants.CountAsync();
        var scopesBefore = await dbContext.AccessGrantAssets.CountAsync();
        var auditsBefore = await dbContext.AuditEvents.CountAsync();
        await using (var failing = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await new AccessGrantRepository(failing).CreateForUserAsync(
                    operatorUser.Id,
                    new CreateAccessGrantCommand(grantCatalogRole.Id,
                        AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
                    new SecurityActorContext(secondAdministrator.Id,
                        new string('x', 257), "audit-failure"), clock.GetUtcNow());
                throw new InvalidOperationException("Invalid audit identity was accepted.");
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: "22001" })
            {
            }
        }
        if (await dbContext.AccessGrants.CountAsync() != grantsBefore ||
            await dbContext.AccessGrantAssets.CountAsync() != scopesBefore ||
            await dbContext.AuditEvents.CountAsync() != auditsBefore)
        {
            throw new InvalidOperationException("Failed audit left partial grant creation.");
        }

        var draftAccessBefore = await service.GetRequiredAsync(operatorUser.Id);
        var persisted = await accessGrantService.CreateForUserAsync(
            operatorUser.Id,
            new CreateAccessGrantCommand(
                grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
            assignmentActor);

        // A custom role already referenced by an active entitlement must not
        // silently gain new permissions. Retire the assignment first.
        try
        {
            await roleCatalog.UpdateAsync(
                grantCatalogRole.Id, grantCatalogRole.Revision,
                new RoleMutationCommand(
                    grantCatalogRole.Name, grantCatalogRole.Description,
                    [WellSitePermissions.AssetsRead], true),
                actor);
            throw new InvalidOperationException(
                "Editing an assigned role changed its effective permissions.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_ROLE_ASSIGNED")
        {
        }

        if (!persisted.IsActive || persisted.Revision != 1 ||
            persisted.SubjectKind != AccessGrantSubjectKind.User ||
            persisted.UserId != operatorUser.Id ||
            !persisted.AssetIds.SequenceEqual([assetId]))
        {
            throw new InvalidOperationException("Persisted direct scoped grant did not round-trip.");
        }

        await using (var reloadedContext = new WellSiteAutoPilotDbContext(options))
        {
            var reloadedService = new AccessGrantAdministrationService(
                new AccessGrantRepository(reloadedContext),
                new UserAccessRepository(reloadedContext),
                new RoleCatalogRepository(reloadedContext),
                clock);
            var loaded = await reloadedService.ListForUserAsync(
                operatorUser.Id, assignmentActor);
            if (loaded.Count != 1 || loaded.Single().Id != persisted.Id ||
                loaded.Single().AssetIds.Single() != assetId)
            {
                throw new InvalidOperationException("Durable scoped grant was not visible after reload.");
            }
        }

        var draftAccessAfter = await service.GetRequiredAsync(operatorUser.Id);
        if (!draftAccessAfter.Roles.SequenceEqual(draftAccessBefore.Roles) ||
            !draftAccessAfter.AssetScopeIds.SequenceEqual(draftAccessBefore.AssetScopeIds))
        {
            throw new InvalidOperationException("New grant unexpectedly changed effective legacy access.");
        }

        foreach (var invalidGrant in new[]
        {
            new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [], grantCatalogRole.Revision),
            new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets,
                [otherAssetId, otherAssetId], grantCatalogRole.Revision),
            new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.AllAssets,
                [assetId], grantCatalogRole.Revision),
            new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.DeploymentWide,
                [assetId], grantCatalogRole.Revision),
            new CreateAccessGrantCommand(Guid.NewGuid(), AccessGrantScopeKind.ExactAssets,
                [assetId], 1)
        })
        {
            try
            {
                await accessGrantService.CreateForUserAsync(
                    operatorUser.Id, invalidGrant, assignmentActor);
                throw new InvalidOperationException("Invalid scoped grant was accepted.");
            }
            catch (WellSiteAutoPilotException exception)
                when (exception.Kind == FailureKind.Validation)
            {
            }
        }

        try
        {
            await accessGrantService.CreateForUserAsync(
                secondAdministrator.Id,
                new CreateAccessGrantCommand(
                    grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
                assignmentActor);
            throw new InvalidOperationException("Administrator assigned their own grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN")
        {
        }

        // The original bootstrap admin was deliberately demoted earlier in this
        // suite. Historical administrator identity must not bypass current access.
        try
        {
            await accessGrantService.CreateForUserAsync(
                operatorUser.Id,
                new CreateAccessGrantCommand(
                    grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
                new SecurityActorContext(
                    administrator.Id, administrator.IdentityName, "demoted-admin"));
            throw new InvalidOperationException("Demoted administrator created security grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_ADMIN_REQUIRED")
        {
        }

        try
        {
            await accessGrantService.CreateForUserAsync(
                operatorUser.Id,
                new CreateAccessGrantCommand(
                    grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
                new SecurityActorContext(
                    operatorUser.Id, operatorUser.IdentityName, "not-admin"));
            throw new InvalidOperationException("Non-admin created security grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_ADMIN_REQUIRED")
        {
        }

        try
        {
            await accessGrantService.CreateForUserAsync(
                operatorUser.Id,
                new CreateAccessGrantCommand(
                    grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [assetId], grantCatalogRole.Revision),
                assignmentActor);
            throw new InvalidOperationException("Duplicate active grant was accepted.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_GRANT_CONFLICT")
        {
        }

        // A demoted historical administrator must not be able to retire grants.
        try
        {
            await accessGrantService.DeactivateForUserAsync(
                operatorUser.Id, persisted.Id, persisted.Revision,
                new SecurityActorContext(
                    administrator.Id, administrator.IdentityName, "retire-demoted-admin"));
            throw new InvalidOperationException("Demoted administrator retired a grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_ADMIN_REQUIRED")
        {
        }
        try
        {
            await accessGrantService.DeactivateForUserAsync(
                secondAdministrator.Id, persisted.Id, persisted.Revision, assignmentActor);
            throw new InvalidOperationException("Administrator retired their own grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN")
        {
        }

        var inactiveRole = await roleCatalog.UpdateAsync(
            grantCatalogRole.Id, grantCatalogRole.Revision,
            new RoleMutationCommand(grantCatalogRole.Name, "Retired role",
                grantCatalogRole.Permissions, false), assignmentActor);
        try
        {
            await roleCatalog.UpdateAsync(inactiveRole.Id, inactiveRole.Revision,
                new RoleMutationCommand(inactiveRole.Name, "Reactivated role",
                    inactiveRole.Permissions, true), assignmentActor);
            throw new InvalidOperationException("Role reactivation revived an assigned grant.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_ROLE_ASSIGNED")
        {
        }

        // Retirement and its audit must roll back together as well.
        auditsBefore = await dbContext.AuditEvents.CountAsync();
        await using (var failing = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await new AccessGrantRepository(failing).DeactivateForUserAsync(
                    operatorUser.Id, persisted.Id, persisted.Revision,
                    new SecurityActorContext(secondAdministrator.Id,
                        new string('x', 257), "retirement-audit-failure"), clock.GetUtcNow());
                throw new InvalidOperationException("Invalid retirement audit was accepted.");
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: "22001" })
            {
            }
        }
        var afterFailedRetirement = await dbContext.AccessGrants.AsNoTracking()
            .SingleAsync(item => item.Id == persisted.Id);
        if (!afterFailedRetirement.IsActive || afterFailedRetirement.Revision != 1 ||
            await dbContext.AuditEvents.CountAsync() != auditsBefore)
        {
            throw new InvalidOperationException("Failed audit left partial retirement.");
        }

        // Deactivation is an explicit, revision-fenced, audited draft-only mutation.
        var deactivated = await accessGrantService.DeactivateForUserAsync(
            operatorUser.Id, persisted.Id, persisted.Revision, assignmentActor);
        if (deactivated.IsActive || deactivated.Revision != 2 ||
            deactivated.AssetIds.Single() != assetId)
        {
            throw new InvalidOperationException("Versioned grant deactivation failed.");
        }
        try
        {
            await accessGrantService.DeactivateForUserAsync(
                operatorUser.Id, persisted.Id, persisted.Revision, assignmentActor);
            throw new InvalidOperationException("Stale deactivation was accepted.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_GRANT_CONFLICT")
        {
        }
        await using (var verification = new WellSiteAutoPilotDbContext(options))
        {
            var inactive = await new AccessGrantRepository(verification)
                .ListForUserAsync(operatorUser.Id);
            if (inactive.Count != 1 || inactive.Single().IsActive ||
                inactive.Single().Revision != 2)
            {
                throw new InvalidOperationException("Inactive grant did not round-trip.");
            }
            var audit = await verification.AuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Action == "security.grant.deactivated" &&
                    item.TargetId == persisted.Id.ToString("D"));
            if (audit is null || audit.ActorUserId != secondAdministrator.Id ||
                audit.CorrelationId != "grant-test")
            {
                throw new InvalidOperationException("Deactivation audit missing.");
            }
        }

        var grantAudit = await dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.Action == "security.grant.created" &&
                item.TargetId == persisted.Id.ToString("D"))
            .SingleOrDefaultAsync();
        if (grantAudit is null ||
            grantAudit.ActorUserId != secondAdministrator.Id ||
            grantAudit.CorrelationId != "grant-test")
        {
            throw new InvalidOperationException("New scoped grant missing audited actor/provenance.");
        }

        using var grantDetails = JsonDocument.Parse(grantAudit.DetailsJson);
        if (grantDetails.RootElement.GetProperty("roleRevision").GetInt64() != grantCatalogRole.Revision)
        {
            throw new InvalidOperationException("Grant audit lost the reviewed role revision.");
        }

        var restoredRole = await roleCatalog.UpdateAsync(
            inactiveRole.Id, inactiveRole.Revision,
            new RoleMutationCommand(inactiveRole.Name, "Grants retired",
                inactiveRole.Permissions, true), assignmentActor);
        if (!restoredRole.IsActive || restoredRole.Revision != inactiveRole.Revision + 1)
        {
            throw new InvalidOperationException("Unassigned role could not be reactivated.");
        }

        // Separate DbContexts race on the same observed revision. Exactly one
        // retirement and one audit may commit; the loser must see a conflict.
        var racingGrant = await accessGrantService.CreateForUserAsync(
            operatorUser.Id, new CreateAccessGrantCommand(restoredRole.Id,
                AccessGrantScopeKind.ExactAssets, [assetId], restoredRole.Revision), assignmentActor);
        await using var firstWriter = new WellSiteAutoPilotDbContext(options);
        await using var secondWriter = new WellSiteAutoPilotDbContext(options);
        var outcomes = await Task.WhenAll(RetireAsync(firstWriter), RetireAsync(secondWriter));
        var raced = await dbContext.AccessGrants.AsNoTracking()
            .SingleAsync(item => item.Id == racingGrant.Id);
        if (outcomes.Count(item => item) != 1 || raced.IsActive || raced.Revision != 2 ||
            await dbContext.AuditEvents.CountAsync(item =>
                item.TargetId == racingGrant.Id.ToString("D") &&
                item.Action == "security.grant.deactivated") != 1)
        {
            throw new InvalidOperationException("Concurrent retirement did not have one audited winner.");
        }

        await WindowsGroupAdministrationChecks.RunAsync(options, secondAdministrator, operatorUser, assetId, clock);

        async Task<bool> RetireAsync(WellSiteAutoPilotDbContext writer)
        {
            try
            {
                await new AccessGrantRepository(writer).DeactivateForUserAsync(
                    operatorUser.Id, racingGrant.Id, racingGrant.Revision,
                    assignmentActor, clock.GetUtcNow());
                return true;
            }
            catch (WellSiteAutoPilotException exception)
                when (exception.Code == "SECURITY_GRANT_CONFLICT")
            {
                return false;
            }
        }
    }
}
