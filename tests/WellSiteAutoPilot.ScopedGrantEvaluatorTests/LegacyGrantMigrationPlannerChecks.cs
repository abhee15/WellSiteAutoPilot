using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

internal static class LegacyGrantMigrationPlannerChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        IReadOnlySet<Guid> activeAssets = new HashSet<Guid> { assetA, assetB };
        var kinds = Enum.GetValues<ApplicationRole>();
        var roles = kinds.Select(kind => new RoleDefinition(Guid.NewGuid(), kind.ToString(),
            kind.ToString(), "", true, true, 1, now, now,
            WellSitePermissions.All.Where(permission =>
                RolePermissionMap.HasPermission([kind], permission)).ToArray())).ToArray();
        var user = new UserAccessProfile(Guid.NewGuid(), "test-user", "FIELD\\operator",
            "FIELD\\OPERATOR", "Test Operator", true, now, now, [], []);

        // Every legacy role combination, with empty, single and multi-Asset
        // scopes, must reproduce all known resource and global decisions.
        for (var mask = 0; mask < (1 << kinds.Length); mask++)
        {
            var assignedRoles = kinds.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            foreach (var scopes in new Guid[][] { [], [assetA], [assetA, assetB] })
            {
                var subject = user with { Roles = assignedRoles, AssetScopeIds = scopes };
                var plan = LegacyGrantMigrationPlanner.PreviewForKnownAssets(subject, roles, activeAssets);
                if (plan.Any(grant => grant.SubjectKind != AccessGrantSubjectKind.User ||
                    grant.UserId != subject.Id || grant.GroupSid is not null ||
                    !grant.IsActive || grant.Revision != 1) ||
                    plan.Select(grant => grant.Id).Distinct().Count() != plan.Count)
                {
                    throw new InvalidOperationException("Migration rehearsal produced malformed assignments.");
                }
                foreach (var grant in plan.Where(grant => grant.ScopeKind == AccessGrantScopeKind.AllAssets))
                {
                    if (roles.Single(role => role.Id == grant.RoleId).Name != "Admin" || grant.AssetIds.Count != 0)
                    {
                        throw new InvalidOperationException("AllAssets escaped the Admin assignment.");
                    }
                }
                foreach (var target in new Guid?[] { null, assetA, assetB })
                {
                    var comparison = ScopedAccessComparisonEvaluator.Compare(
                        subject, target, null, activeAssets, plan, roles);
                    if (!comparison.IsDiagnosticOnly || comparison.WideningCount != 0 || comparison.ReductionCount != 0 ||
                        comparison.Permissions.Select(item => item.Permission).Distinct().Count() != comparison.Permissions.Count)
                    {
                        throw new InvalidOperationException("Rehearsal changed legacy access or duplicated a permission.");
                    }
                    if (target is null && comparison.Permissions.Any(item => item.ScopeKind != "DeploymentWide"))
                    {
                        throw new InvalidOperationException("Global-only comparison disclosed resource decisions.");
                    }
                }
            }
        }

        var operatorUser = user with { Roles = [ApplicationRole.Operator], AssetScopeIds = [assetA] };
        foreach (var malformed in new[]
        {
            operatorUser with { Id = Guid.Empty }, operatorUser with { IsActive = false },
            operatorUser with { Roles = [ApplicationRole.Operator, ApplicationRole.Operator] },
            operatorUser with { Roles = [(ApplicationRole)99] },
            operatorUser with { AssetScopeIds = [assetA, assetA] },
            operatorUser with { AssetScopeIds = [Guid.NewGuid()] },
            operatorUser with { AssetScopeIds = [Guid.Empty] },
        })
        {
            Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(malformed, roles, activeAssets));
        }
        Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, roles, new HashSet<Guid> { assetA, Guid.Empty }));
        Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, [], activeAssets));
        Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, roles.Concat(roles).ToArray(), activeAssets));
        var drifted = roles.Select(role => role.Name == "Operator"
            ? role with { Permissions = [WellSitePermissions.SecurityManage] } : role).ToArray();
        Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, drifted, activeAssets));
        var inactive = roles.Select(role => role.Name == "Operator" ? role with { IsActive = false } : role).ToArray();
        Reject(() => LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, inactive, activeAssets));

        var expectedGrants = LegacyGrantMigrationPlanner.PreviewForKnownAssets(operatorUser, roles, activeAssets);
        var snapshot = new LegacyMigrationSnapshot(operatorUser, roles, activeAssets, expectedGrants);
        var matched = LegacyMigrationPreflightEvaluator.Evaluate(snapshot);
        if (!matched.IsDiagnosticOnly || !matched.RecordedGrantsMatchLegacy ||
            matched.ExpansionCount != 0 || matched.ReductionCount != 0 ||
            matched.DifferenceSamples.Count != 0 || matched.RecordedGrantCount != expectedGrants.Count)
        {
            throw new InvalidOperationException("Pure migration parity rejected equivalent grants.");
        }
        var missing = LegacyMigrationPreflightEvaluator.Evaluate(snapshot with { RecordedGrants = [] });
        if (missing.RecordedGrantsMatchLegacy || missing.ReductionCount < 1 || missing.ExpansionCount != 0)
        {
            throw new InvalidOperationException("Pure migration parity missed absent grants.");
        }
        var otherUserGrants = expectedGrants.Select(grant => grant with { UserId = Guid.NewGuid() }).ToArray();
        var crossed = LegacyMigrationPreflightEvaluator.Evaluate(snapshot with { RecordedGrants = otherUserGrants });
        if (crossed.RecordedGrantsMatchLegacy || crossed.ReductionCount != missing.ReductionCount)
        {
            throw new InvalidOperationException("Pure migration parity borrowed another user's grants.");
        }

        var manyAssets = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid())
            .Append(assetA).Append(assetB).ToHashSet();
        var resourceOnly = expectedGrants.Where(grant => grant.ScopeKind == AccessGrantScopeKind.ExactAssets).ToArray();
        var noGlobal = LegacyMigrationPreflightEvaluator.Evaluate(snapshot with
        {
            ActiveAssetIds = manyAssets, RecordedGrants = resourceOnly
        });
        var globalCount = ProductPermissionCatalog.Definitions.Count(permission =>
            permission.ScopeKind == "DeploymentWide" &&
            RolePermissionMap.HasPermission([ApplicationRole.Operator], permission.Key));
        if (noGlobal.ReductionCount != globalCount || noGlobal.ExpansionCount != 0)
        {
            throw new InvalidOperationException("Global differences were multiplied by the Asset count.");
        }
        var broadened = new AccessGrant(Guid.NewGuid(), AccessGrantSubjectKind.User,
            operatorUser.Id, null, roles.Single(role => role.Name == "Engineer").Id,
            AccessGrantScopeKind.AllAssets, [], true, 1);
        var wide = LegacyMigrationPreflightEvaluator.Evaluate(snapshot with
        {
            ActiveAssetIds = manyAssets, RecordedGrants = expectedGrants.Append(broadened).ToArray()
        });
        if (wide.RecordedGrantsMatchLegacy || wide.ExpansionCount <= 20 || wide.DifferenceSamples.Count != 20)
        {
            throw new InvalidOperationException("Migration parity lost exact counts or bounded difference samples.");
        }

        var engineer = roles.Single(role => role.Name == "Engineer");
        var expanding = new AccessGrant(Guid.NewGuid(), AccessGrantSubjectKind.User,
            operatorUser.Id, null, engineer.Id, AccessGrantScopeKind.ExactAssets, [assetA], true, 1);
        var inspection = ScopedAccessComparisonEvaluator.Compare(operatorUser, assetA,
            "Well Alpha", activeAssets, [expanding], roles);
        var management = inspection.Permissions.Single(item => item.Permission == WellSitePermissions.AssetsManage);
        if (!management.ProposedAllowed || management.LegacyAllowed || inspection.WideningCount == 0 ||
            management.ProposedGrantId != expanding.Id)
        {
            throw new InvalidOperationException("Pure inspection failed to identify a privilege expansion.");
        }
        var crossUser = ScopedAccessComparisonEvaluator.Compare(operatorUser, assetA,
            "Well Alpha", activeAssets, [expanding with { UserId = Guid.NewGuid() }], roles);
        if (crossUser.Permissions.Any(item => item.ProposedAllowed))
        {
            throw new InvalidOperationException("Inspection combined another user's grants.");
        }
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Malformed legacy migration rehearsal was accepted.");
        }
        catch (WellSiteAutoPilotException exception)
            when (exception.Code == "SECURITY_LEGACY_MIGRATION_PARITY_FAILED" && exception.Kind == FailureKind.Validation)
        {
        }
    }
}
