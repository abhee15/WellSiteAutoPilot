using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public sealed class AccessGrantAdministrationService(
    IAccessGrantRepository grants,
    IUserAccessRepository users,
    IRoleCatalogRepository roles,
    TimeProvider clock)
{
    public Task<AccessGrant> CreateAuthorizedAsync(AccessGrantSubject subject, CreateAccessGrantCommand command,
        AccessSubject administrator, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.AssetIds);
        if (command.ExpectedRoleRevision < 1)
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException("SECURITY_GRANT_ROLE_REVISION_REQUIRED",
                WellSiteAutoPilot.Failures.FailureKind.Validation, "The reviewed role revision is required.");
        return grants.CreateAuthorizedAsync(subject, command, administrator, actor, clock.GetUtcNow(), cancellationToken);
    }

    public Task<AccessGrant> DeactivateAuthorizedAsync(AccessGrantSubject subject, Guid grantId, long expectedRevision,
        AccessSubject administrator, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        if (grantId == Guid.Empty || expectedRevision < 1)
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException("SECURITY_GRANT_REVISION_INVALID",
                WellSiteAutoPilot.Failures.FailureKind.Validation, "A valid grant and positive revision are required.");
        return grants.DeactivateAuthorizedAsync(subject, grantId, expectedRevision, administrator, actor, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<IReadOnlyCollection<AccessGrant>> ListAsync(
        AccessGrantSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(actor, cancellationToken);
        await RequireSubjectAsync(subject, cancellationToken);
        return await grants.ListAsync(subject, cancellationToken);
    }

    public async Task<AccessGrant> CreateAsync(
        AccessGrantSubject subject, CreateAccessGrantCommand command,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.AssetIds);
        await RequireAdministratorAsync(actor, cancellationToken);

        if (subject.UserId == actor.UserId)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN",
                WellSiteAutoPilot.Failures.FailureKind.Authorization,
                "Administrators may not assign roles to themselves.");
        }

        await RequireSubjectAsync(subject, cancellationToken);
        if (subject.GroupId is { } groupId)
            SecurityActorGroupGuard.RequireOtherGroup(actor, groupId, clock.GetUtcNow(), subject.DirectoryId!);
        if (command.ExpectedRoleRevision < 1)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_GRANT_ROLE_REVISION_REQUIRED",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "The reviewed role revision is required.");
        }
        var role = await roles.GetAsync(command.RoleId, cancellationToken);
        if (role is null || !role.IsActive)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_ROLE_INVALID",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "An active role definition is required.");
        }

        if (role.Revision != command.ExpectedRoleRevision)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_GRANT_ROLE_CONFLICT",
                WellSiteAutoPilot.Failures.FailureKind.Conflict,
                "Role changed since review. Reload and review its permissions again.");
        }

        if (command.AssetIds.Count > ScopedGrantEvaluator.MaximumExactAssetsPerGrant ||
            command.AssetIds.Any(id => id == Guid.Empty) ||
            command.AssetIds.Distinct().Count() != command.AssetIds.Count)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_GRANT_ASSETS_INVALID",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "Grant Asset IDs must be distinct, valid, and within allowed bounds.");
        }

        var scopeName = command.ScopeKind switch
        {
            AccessGrantScopeKind.ExactAssets => "ResourceScoped",
            AccessGrantScopeKind.AllAssets => "ResourceScoped",
            AccessGrantScopeKind.DeploymentWide => "DeploymentWide",
            _ => ""
        };

        if (string.IsNullOrEmpty(scopeName) ||
            (command.ScopeKind == AccessGrantScopeKind.ExactAssets &&
             command.AssetIds.Count == 0) ||
            (command.ScopeKind != AccessGrantScopeKind.ExactAssets &&
             command.AssetIds.Count != 0) ||
            !role.Permissions.Any(permission => ProductPermissionCatalog.Definitions.Any(
                item => item.Key == permission && item.ScopeKind == scopeName)))
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_GRANT_SCOPE_INVALID",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "Role permissions and Asset assignment scope are incompatible.");
        }

        // This is an additive, auditable grant record only. It does not
        // change legacy claims or production resource authorization yet.
        return await grants.CreateAsync(
            subject, command, actor, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<AccessGrant> DeactivateAsync(
        AccessGrantSubject subject, Guid grantId, long expectedRevision,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync(actor, cancellationToken);
        if (actor.UserId == subject.UserId)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN",
                WellSiteAutoPilot.Failures.FailureKind.Authorization,
                "Administrators may not change their own assignments.");
        }
        await RequireSubjectAsync(subject, cancellationToken);
        if (subject.GroupId is { } groupId)
            SecurityActorGroupGuard.RequireOtherGroup(actor, groupId, clock.GetUtcNow(), subject.DirectoryId!);
        if (grantId == Guid.Empty || expectedRevision < 1)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_GRANT_REVISION_INVALID",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "A valid grant and positive revision are required.");
        }
        return await grants.DeactivateAsync(
            subject, grantId, expectedRevision, actor, clock.GetUtcNow(),
            cancellationToken);
    }

    public Task<IReadOnlyCollection<AccessGrant>> ListForUserAsync(Guid userId, SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        ListAsync(AccessGrantSubject.ForUser(userId), actor, cancellationToken);

    public Task<AccessGrant> CreateForUserAsync(Guid userId, CreateAccessGrantCommand command, SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        CreateAsync(AccessGrantSubject.ForUser(userId), command, actor, cancellationToken);

    public Task<AccessGrant> DeactivateForUserAsync(Guid userId, Guid grantId, long expectedRevision, SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        DeactivateAsync(AccessGrantSubject.ForUser(userId), grantId, expectedRevision, actor, cancellationToken);

    private async Task RequireSubjectAsync(AccessGrantSubject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.UserId is { } userId)
        {
            _ = await RequireActiveUserAsync(userId, cancellationToken);
        }
    }

    private async Task<UserAccessProfile> RequireActiveUserAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE",
                WellSiteAutoPilot.Failures.FailureKind.NotFound,
                "The user is not active or does not exist.");
        }

        return user;
    }

    public async Task RequireAdministratorAsync(
        SecurityActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId is not { } adminId)
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED",
                WellSiteAutoPilot.Failures.FailureKind.Authorization,
                "An authenticated administrator is required.");
        }

        var user = await users.GetAsync(adminId, cancellationToken);
        if (user is null || !user.IsActive ||
            !user.Roles.Contains(ApplicationRole.Admin))
        {
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED",
                WellSiteAutoPilot.Failures.FailureKind.Authorization,
                "Only an active administrator can manage grants.");
        }
    }
}
