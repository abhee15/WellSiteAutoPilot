#!/usr/bin/env python3
"""CI guard: all product API endpoints must have reviewed grant policy metadata.

This static inventory is a *regression tripwire*, not proof of scoped-grant
enforcement. Runtime integration tests and SQL-before-pagination tests remain
mandatory before any authorization cutover.
"""
from __future__ import annotations

from pathlib import Path
import re
import sys

# Freeze the reviewed API surface. New operations and changed policy metadata
# must be consciously reviewed and added here in the SAME PR.
# The two authenticated-only endpoints inherit the authenticated route group.
EXPECTED_ENDPOINTS: dict[tuple[str, str], tuple[str, str | None]] = {
    ("GET", "/system/info"): ("GetSystemInformation", "SystemStatusRead"),
    ("GET", "/system/health"): ("GetSystemHealth", None),
    ("GET", "/security/me"): ("GetCurrentUser", None),
    ("GET", "/security/users"): ("ListSecurityUsers", "SecurityManage"),
    ("GET", "/security/audit-events"): ("ListAuditEvents", "SecurityManage"),
    ("GET", "/security/role-permissions"): ("ListProductPermissions", "SecurityManage"),
    ("GET", "/security/roles"): ("ListSecurityRoles", "SecurityManage"),
    ("GET", "/security/roles/{roleId:guid}"): ("GetSecurityRole", "SecurityManage"),
    ("POST", "/security/roles"): ("CreateSecurityRole", "SecurityManage"),
    ("PUT", "/security/roles/{roleId:guid}"): ("UpdateSecurityRole", "SecurityManage"),
    ("GET", "/security/users/{userId:guid}/grants"): ("ListUserAccessGrants", "SecurityManage"),
    ("POST", "/security/users/{userId:guid}/grants"): ("CreateUserAccessGrant", "SecurityManage"),
    ("PUT", "/security/users/{userId:guid}/grants/{grantId:guid}/deactivate"): ("DeactivateUserAccessGrant", "SecurityManage"),
    ("GET", "/security/groups"): ("ListAssignedGroups", "SecurityManage"),
    ("GET", "/security/directory"): ("GetGroupDirectoryCapabilities", "SecurityManage"),
    ("GET", "/security/directory/groups"): ("SearchDirectoryGroups", "SecurityManage"),
    ("GET", "/security/directories/{directoryId}/group-grants"): ("ListGroupAccessGrants", "SecurityManage"),
    ("POST", "/security/directories/{directoryId}/group-grants"): ("CreateGroupAccessGrant", "SecurityManage"),
    ("PUT", "/security/directories/{directoryId}/group-grants/{grantId:guid}/deactivate"): ("DeactivateGroupAccessGrant", "SecurityManage"),
    ("POST", "/asset-types"): ("CreateAssetType", "AssetTypesManage"),
    ("PUT", "/asset-types/{assetTypeId:guid}/hide"): ("HideAssetType", "AssetTypesHide"),
    ("PUT", "/asset-types/{assetTypeId:guid}/unhide"): ("UnhideAssetType", "AssetTypesManage"),
    ("DELETE", "/asset-types/{assetTypeId:guid}"): ("DeleteAssetType", "AssetTypesManage"),
    ("GET", "/asset-types"): ("ListAssetTypes", "AssetsRead"),
    ("GET", "/asset-types/{assetTypeId:guid}"): ("GetAssetType", "AssetsRead"),
    ("GET", "/connections"): ("ListConnections", "ConnectionsRead"),
    ("GET", "/connections/adapters"): ("ListConnectionAdapters", "ConnectionsRead"),
    ("GET", "/connections/{connectionId:guid}"): ("GetConnection", "ConnectionsRead"),
    ("POST", "/connections"): ("CreateConnection", "ConnectionsManage"),
    ("PUT", "/connections/{connectionId:guid}"): ("UpdateConnection", "ConnectionsManage"),
    ("PUT", "/connections/{connectionId:guid}/enabled"): ("SetConnectionEnabled", "ConnectionsManage"),
    ("GET", "/connections/{connectionId:guid}/command-catalog"): ("GetConnectionCommandCatalog", "ConnectionsRead"),
    ("PUT", "/connections/{connectionId:guid}/command-catalog"): ("SetConnectionCommandCatalog", "ConnectionsManage"),
    ("POST", "/connections/{connectionId:guid}/test"): ("TestConnection", "ConnectionsManage"),
    ("POST", "/connections/{connectionId:guid}/discover"): ("DiscoverConnection", "ConnectionsManage"),
    ("POST", "/connections/{connectionId:guid}/discover/search"): ("SearchDiscoveredObjects", "ConnectionsManage"),
    ("POST", "/connections/{connectionId:guid}/source-checks"): ("CheckConnectionSourceChanges", "ConnectionsManage"),
    ("GET", "/connections/{connectionId:guid}/source-changes"): ("ReviewConnectionSourceChanges", "ConnectionsRead"),
    ("POST", "/connections/{connectionId:guid}/import/preview"): ("PreviewConnectionImport", "ConnectionsManage"),
    ("POST", "/connections/{connectionId:guid}/import"): ("ImportFromConnection", "ConnectionsManage"),
    ("DELETE", "/connections/{connectionId:guid}"): ("DeleteConnection", "ConnectionsManage"),
    ("GET", "/assets/capabilities"): ("GetAssetCreationCapabilities", "AssetsRead"),
    ("POST", "/assets"): ("CreateAsset", "AssetsManage"),
    ("GET", "/security/assets/directory"): ("ListSecurityAssetDirectory", "SecurityManage"),
    ("GET", "/assets"): ("ListAssets", "AssetsRead"),
    ("GET", "/assets/children"): ("ListAssetChildren", "AssetsRead"),
    ("GET", "/assets/owners"): ("ListAssetOwners", "AssetsRead"),
    ("GET", "/assets/search"): ("SearchAssets", "AssetsRead"),
    ("GET", "/asset-views"): ("ListAssetViews", "ConnectionsRead"),
    ("POST", "/asset-views"): ("CreateAssetView", "AssetViewsCreate"),
    ("GET", "/asset-views/{viewId:guid}"): ("GetAssetView", "ConnectionsRead"),
    ("PUT", "/asset-views/{viewId:guid}"): ("UpdateAssetView", "AssetViewsEdit"),
    ("DELETE", "/asset-views/{viewId:guid}"): ("DeleteAssetView", "AssetViewsDelete"),
    ("GET", "/asset-views/{viewId:guid}/members"): ("ListAssetViewMembers", "ConnectionsRead"),
    ("GET", "/asset-views/{viewId:guid}/selection"): ("GetAssetViewSelection", "ConnectionsRead"),
    ("GET", "/assets/{assetId:guid}"): ("GetAsset", "AssetsRead"),
    ("GET", "/assets/{assetId:guid}/source"): ("GetAssetSource", "AssetsRead"),
    ("POST", "/configured-logic"): ("CreateConfiguredLogic", "ConfiguredLogicManage"),
    ("GET", "/configured-logic"): ("ListConfiguredLogic", "ConfiguredLogicRead"),
    ("GET", "/configured-logic/{configuredLogicId:guid}/activity"): ("GetConfiguredLogicActivity", "ConfiguredLogicRead"),
    ("GET", "/configured-logic/{configuredLogicId:guid}"): ("GetConfiguredLogic", "ConfiguredLogicRead"),
    ("GET", "/configured-logic/{configuredLogicId:guid}/schedule"): ("GetConfiguredLogicSchedule", "ConfiguredLogicRead"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/schedule/pause"): ("PauseConfiguredLogicSchedule", "ConfiguredLogicManage"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/schedule/resume"): ("ResumeConfiguredLogicSchedule", "ConfiguredLogicManage"),
    ("PUT", "/configured-logic/{configuredLogicId:guid}/schedule"): ("ChangeConfiguredLogicScheduleFrequency", "ConfiguredLogicManage"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/revisions"): ("CreateConfiguredLogicRevision", "ConfiguredLogicManage"),
    ("PUT", "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}"): ("UpdateConfiguredLogicDraft", "ConfiguredLogicManage"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/validate"): ("ValidateConfiguredLogicRevision", "ConfiguredLogicManage"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/activate"): ("ActivateConfiguredLogicRevision", "ConfiguredLogicManage"),
    ("POST", "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/shadow"): ("RequestConfiguredShadowExecution", "ExecutionsRequest"),
    ("GET", "/assets/{assetId:guid}/logic"): ("GetAssetLogicSummary", "AssetsRead"),
    ("GET", "/executions/history"): ("GetExecutionHistory", "ExecutionsRead"),
    ("GET", "/executions"): ("ListExecutions", "ExecutionsRead"),
    ("GET", "/executions/{executionId:guid}"): ("GetExecution", "ExecutionsRead"),
    ("POST", "/logic-modules"): ("RegisterLogicModule", "LogicManage"),
    ("POST", "/logic-modules/packages"): ("UploadLogicModulePackage", "LogicManage"),
    ("GET", "/logic-modules"): ("ListLogicModules", "LogicRead"),
    ("GET", "/logic-modules/{moduleId}/{moduleVersion}"): ("GetLogicModule", "LogicRead"),
    ("GET", "/logic-modules/{moduleId}/{moduleVersion}/package-state"): ("GetLogicModulePackageState", "LogicRead"),
    ("POST", "/logic-modules/{moduleId}/{moduleVersion}/approve"): ("ApproveLogicModule", "LogicManage"),
    ("POST", "/logic-modules/{moduleId}/{moduleVersion}/retire"): ("RetireLogicModule", "LogicManage"),
    ("GET", "/recommendations"): ("ListRecommendations", "RecommendationsRead"),
    ("GET", "/recommendations/{recommendationId:guid}"): ("GetRecommendation", "RecommendationsRead"),
    ("POST", "/recommendations/{recommendationId:guid}/decision"): ("DecideRecommendation", "RecommendationsDecide"),
    ("GET", "/control/kill-switch"): ("GetControlKillSwitch", "RecommendationsRead"),
    ("PUT", "/control/kill-switch"): ("SetControlKillSwitch", "ControlManage"),
    ("GET", "/control/policies/{configuredLogicId:guid}"): ("GetAutonomyPolicy", "RecommendationsRead"),
    ("PUT", "/control/policies/{configuredLogicId:guid}"): ("SetAutonomyPolicy", "ControlManage"),
    ("GET", "/control/actions"): ("ListControlActions", "RecommendationsRead"),
    ("POST", "/control/recommendations/{recommendationId:guid}/execute"): ("ExecuteApprovedRecommendation", "ControlManage"),
    ("POST", "/control/actions/{actionId:guid}/verify"): ("VerifyControlAction", "ControlManage"),
    ("POST", "/control/actions/{actionId:guid}/resolve"): ("ResolveControlAction", "ControlManage"),
}

_ROUTE = re.compile(
    r'\b(?P<receiver>[A-Za-z_]\w*)\.Map(?P<method>Get|Post|Put|Patch|Delete)'
    r'\(\s*"(?P<route>/[^"]+)"',
    re.MULTILINE,
)
_NAME = re.compile(r'\.WithName\(\s*"([^"]+)"\s*\)')
_NAMED_POLICY = re.compile(
    r'\.RequireAuthorization\(\s*WellSitePolicies\.(\w+)\s*\)'
)
_ANY_POLICY = re.compile(r'\.RequireAuthorization\(')


def validate_routes(source: str) -> list[str]:
    """Return failures; never infer an absent policy from the group silently."""
    errors: list[str] = []
    group = re.search(r'\bvar\s+v1\s*=\s*([^;]+);', source, re.DOTALL)
    if (group is None
            or '.MapGroup("/api/v{version:apiVersion}")' not in group.group(1)
            or '.RequireAuthorization()' not in group.group(1)):
        errors.append("Product v1 API group must require authentication.")

    authentication = source.find("app.UseAuthentication();")
    authorization = source.find("app.UseAuthorization();")
    if authentication < 0 or authorization <= authentication:
        errors.append("Authentication middleware must precede authorization middleware.")

    matches = list(_ROUTE.finditer(source))
    found: set[tuple[str, str]] = set()
    used_names: set[str] = set()
    for index, match in enumerate(matches):
        receiver = match.group("receiver")
        method = match.group("method").upper()
        path = match.group("route")
        key = (method, path)
        end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
        # Health endpoints are registered after the final product route.
        # Their AllowAnonymous metadata must not be attributed to that route.
        health_boundary = source.find("\napp.MapHealthChecks(", match.start())
        if health_boundary >= 0:
            end = min(end, health_boundary)
        block = source[match.start():end]

        if receiver != "v1":
            errors.append(f"{receiver}.Map{method}({path}) is outside the reviewed v1 group.")
            continue

        if key in found:
            errors.append(f"Duplicate API route: {method} {path}.")
            continue
        found.add(key)

        expected = EXPECTED_ENDPOINTS.get(key)
        if expected is None:
            errors.append(f"Unreviewed API route: {method} {path}.")
            continue

        expected_name, expected_policy = expected
        names = _NAME.findall(block)
        if names != [expected_name]:
            errors.append(f"{method} {path}: expected endpoint name {expected_name}, found {names}.")
        if names and names[0] in used_names:
            errors.append(f"Duplicate endpoint name: {names[0]}.")
        used_names.update(names)

        if ".AllowAnonymous(" in block:
            errors.append(f"{method} {path}: protected API must not allow anonymous access.")

        declared = _NAMED_POLICY.findall(block)
        count = len(_ANY_POLICY.findall(block))
        if expected_policy is None:
            if count:
                errors.append(f"{method} {path}: authenticated-only policy contract changed.")
        elif declared != [expected_policy] or count != 1:
            errors.append(
                f"{method} {path}: expected exactly one "
                f"WellSitePolicies.{expected_policy}, found {declared} "
                f"({count} authorization declarations)."
            )

    for method, path in sorted(EXPECTED_ENDPOINTS.keys() - found):
        errors.append(f"Reviewed API route missing: {method} {path}.")

    return errors


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    program = root / "src/server/WellSiteAutoPilot.Server/Program.cs"
    try:
        errors = validate_routes(program.read_text(encoding="utf-8"))
    except OSError as error:
        print(f"Cannot verify API policy inventory: {error}", file=sys.stderr)
        return 2
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        print(f"API policy inventory failed with {len(errors)} error(s).", file=sys.stderr)
        return 1
    print(f"API policy inventory verified: {len(EXPECTED_ENDPOINTS)} reviewed endpoints.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
