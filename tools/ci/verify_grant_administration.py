"""Exercise draft-only grant APIs against the running CI Server and PostgreSQL."""

import json
import sys
import urllib.error
import urllib.request
import uuid


BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
ADMIN = r"CI\wsa-admin"
OPERATOR = r"CI\operator-one"


def request(method, path, identity=ADMIN, body=None, expected=200):
    headers = {"X-Correlation-ID": "ci-grant-administration"}
    if identity is not None:
        headers["X-WSA-Test-Identity"] = identity
    if body is not None:
        headers["Content-Type"] = "application/json"
    message = urllib.request.Request(
        BASE_URL + "/api/v1" + path,
        data=None if body is None else json.dumps(body).encode(),
        headers=headers,
        method=method,
    )
    try:
        response = urllib.request.urlopen(message, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        payload = response.read().decode()
        if response.code != expected:
            raise AssertionError(f"{method} {path}: expected {expected}, got {response.code}: {payload}")
        return json.loads(payload) if payload else None


admin = request("GET", "/security/me")
operator = request("GET", "/security/me", OPERATOR)
other = request("GET", "/security/me", r"CI\grant-other")
before_assets = request("GET", "/assets", OPERATOR)
assets = request("GET", "/assets")
outside = next(asset for asset in assets if asset["id"] not in {a["id"] for a in before_assets})
role = request("POST", "/security/roles", body={
    "name": "Grant HTTP " + uuid.uuid4().hex[:12],
    "description": "CI draft-only grant",
    "permissions": ["assets.read", "assets.manage"],
}, expected=201)
path = f"/security/users/{operator['userId']}/grants"
command = {"roleId": role["roleId"], "scopeKind": "ExactAssets", "assetIds": [outside["id"]], "expectedRoleRevision": role["revision"]}
fake_retirement = path + f"/{uuid.uuid4()}/deactivate"

# Every route enforces authentication and administrator authorization.
for identity, status in [(None, 401), (OPERATOR, 403)]:
    request("GET", path, identity, expected=status)
    request("POST", path, identity, command, expected=status)
    request("PUT", fake_retirement, identity, {"expectedRevision": 1}, expected=status)

request("POST", f"/security/users/{admin['userId']}/grants", body=command, expected=403)
request("POST", path, body={**command, "assetIds": []}, expected=400)
request("POST", path, body={**command, "scopeKind": "Unknown"}, expected=400)
request("POST", path, body={**command, "expectedRoleRevision": 0}, expected=400)
role = request("PUT", f"/security/roles/{role['roleId']}", body={
    "expectedRevision": role["revision"], "name": role["name"],
    "description": "Changed after review", "isActive": True,
    "permissions": role["permissions"],
})
request("POST", path, body=command, expected=409)
assert request("GET", path) == []
command["expectedRoleRevision"] = role["revision"]
grant = request("POST", path, body=command, expected=201)
assert grant["userId"] == operator["userId"] and grant["assetIds"] == [outside["id"]]
assert grant["isActive"] and grant["revision"] == 1
assert request("GET", path) == [grant]
assert request("GET", f"/security/users/{other['userId']}/grants") == []
request("POST", path, body=command, expected=409)

# Inspection is server-computed, privileged and diagnostic-only.
inspection_path = f"/security/users/{operator['userId']}/effective-access?assetId={outside['id']}"
request("GET", inspection_path, None, expected=401)
request("GET", inspection_path, OPERATOR, expected=403)
inspection = request("GET", inspection_path)
assert inspection["isDiagnosticOnly"] is True and inspection["wideningCount"] > 0
assert inspection["userId"] == operator["userId"] and inspection["assetId"] == outside["id"]
request("GET", f"/security/users/{uuid.uuid4()}/effective-access", expected=404)
request("GET", f"/security/users/{operator['userId']}/effective-access?assetId={uuid.uuid4()}", expected=404)

# SQL-scoped proposed Assets are privileged, bounded and diagnostic only.
proposed_path = f"/security/users/{operator['userId']}/proposed-assets"
query = proposed_path + "?permission=assets.manage&offset=0&limit=1"
request("GET", query, None, expected=401)
request("GET", query, OPERATOR, expected=403)
proposed = request("GET", query)
assert proposed["isDiagnosticOnly"] is True
assert proposed["userId"] == operator["userId"] and proposed["permission"] == "assets.manage"
assert proposed["offset"] == 0 and proposed["limit"] == 1
assert proposed["assets"] == [{"id": outside["id"], "name": outside["name"]}]
assert request("GET", proposed_path + "?permission=assets.manage&offset=1&limit=1")["assets"] == []
request("GET", proposed_path + "?permission=security.manage", expected=400)
request("GET", proposed_path + "?permission=assets.read&limit=501", expected=400)
request("GET", proposed_path + "?permission=assets.read&offset=-1", expected=400)
request("GET", f"/security/users/{uuid.uuid4()}/proposed-assets?permission=assets.read", expected=404)
assert request("GET", "/assets", OPERATOR) == before_assets

# Bounded complete migration snapshots remain privileged diagnostics.
user_preflight = f"/security/users/{operator['userId']}/migration-preflight"
for route in [user_preflight, "/security/migration-preflight"]:
    request("GET", route, None, expected=401)
    request("GET", route, OPERATOR, expected=403)
preview = request("GET", user_preflight)
assert preview["isDiagnosticOnly"] is True and preview["userId"] == operator["userId"]
assert preview["expansionCount"] > 0 and preview["recordedGrantsMatchLegacy"] is False
assert len(preview["differenceSamples"]) <= 20
request("GET", f"/security/users/{uuid.uuid4()}/migration-preflight", expected=404)
deployment_preview = request("GET", "/security/migration-preflight")
assert deployment_preview["isDiagnosticOnly"] is True
assert deployment_preview["activeUserCount"] == len(deployment_preview["users"])
assert deployment_preview["activeAssetCount"] == preview["activeAssetCount"]
assert deployment_preview["expansionCount"] == sum(u["expansionCount"] for u in deployment_preview["users"])
assert deployment_preview["reductionCount"] == sum(u["reductionCount"] for u in deployment_preview["users"])
assert next(u for u in deployment_preview["users"] if u["userId"] == operator["userId"])["expansionCount"] == preview["expansionCount"]
assert request("GET", path) == [grant]

# A recorded future entitlement must not expand live legacy authorization.
assert request("GET", "/assets", OPERATOR) == before_assets
request("GET", f"/assets/{outside['id']}", OPERATOR, expected=403)
request("POST", "/assets", OPERATOR, {}, expected=403)

role_update = {
    "expectedRevision": role["revision"], "name": role["name"],
    "description": role["description"], "isActive": True,
    "permissions": ["assets.read"],
}
request("PUT", f"/security/roles/{role['roleId']}", body=role_update, expected=409)
request("PUT", f"/security/users/{other['userId']}/grants/{grant['grantId']}/deactivate",
        body={"expectedRevision": 1}, expected=404)
retirement = path + f"/{grant['grantId']}/deactivate"
retired = request("PUT", retirement, body={"expectedRevision": 1})
assert not retired["isActive"] and retired["revision"] == 2
assert retired["assetIds"] == grant["assetIds"]
request("PUT", retirement, body={"expectedRevision": 1}, expected=409)
assert request("GET", path) == [retired]
request("PUT", f"/security/roles/{role['roleId']}", body=role_update)
assert request("GET", "/assets", OPERATOR) == before_assets
print("Grant HTTP authentication, scope isolation, retirement, and legacy authorization checks passed.")

# Full directory access is administrator-only and page inputs are bounded.
request("GET", "/security/assets/directory", None, expected=401)
request("GET", "/security/assets/directory", OPERATOR, expected=403)
request("GET", "/security/assets/directory?offset=-1", expected=400)
request("GET", "/security/assets/directory?limit=501", expected=400)
page_one = request("GET", "/security/assets/directory?offset=0&limit=1")
page_two = request("GET", "/security/assets/directory?offset=1&limit=1")
assert len(page_one) == 1 and len(page_two) == 1
assert page_one[0]["id"] != page_two[0]["id"]
assert set(page_one[0]) == {"id", "name", "isActive"}

# Explicit deployment writes remain audit-only and never change live access.
for route in ["/security/materialize-legacy-grants", "/security/rollback-legacy-grants"]:
    request("POST", route, None, {"confirmed": True}, expected=401)
    request("POST", route, OPERATOR, {"confirmed": True}, expected=403)
    request("POST", route, body={"confirmed": False}, expected=400)
migration = request("POST", "/security/materialize-legacy-grants", body={"confirmed": True})
assert migration["grantsCreated"] > 0
assert migration["recordedGrantsMatchLegacy"] is True and migration["liveAuthorizationUnchanged"] is True
assert request("GET", "/security/migration-preflight")["allActiveUsersMatchRecordedGrants"] is True
assert request("POST", "/security/materialize-legacy-grants", body={"confirmed": True})["grantsCreated"] == 0
assert request("GET", "/assets", OPERATOR) == before_assets
request("GET", f"/assets/{outside['id']}", OPERATOR, expected=403)
rollback = request("POST", "/security/rollback-legacy-grants", body={"confirmed": True})
assert rollback["grantsDeactivated"] == migration["grantsCreated"] and rollback["liveAuthorizationUnchanged"] is True
assert request("POST", "/security/rollback-legacy-grants", body={"confirmed": True})["grantsDeactivated"] == 0
assert request("GET", "/assets", OPERATOR) == before_assets
request("GET", f"/assets/{outside['id']}", OPERATOR, expected=403)
print("Migration HTTP confirmation, parity, idempotency, rollback, and legacy authorization checks passed.")

# Group targets use SID identity, the same role/scope validation, and CAS retirement.
# The SID is an assignment target, never a claim of the caller's membership.
group_sid = 'S-1-5-21-710-720-730-740'
group_path = f'/security/windows-groups/{group_sid}/grants'
for identity, status in [(None, 401), (OPERATOR, 403)]:
    request('GET', group_path, identity, expected=status)
    request('POST', group_path, identity, command, expected=status)
    request('PUT', group_path + f'/{uuid.uuid4()}/deactivate', identity,
            {'expectedRevision': 1}, expected=status)
for invalid_sid in ['not-a-sid', 'S-1-5-01', 'S-1-5-4294967296']:
    request('GET', f'/security/windows-groups/{invalid_sid}/grants', expected=400)
request('POST', group_path, body=command, expected=409)  # role changed above
current_role = request('GET', f"/security/roles/{role['roleId']}")
group_command = {**command, 'expectedRoleRevision': current_role['revision']}
request('POST', group_path, body={**group_command, 'assetIds': []}, expected=400)
request('POST', group_path, body={**group_command, 'scopeKind': 'Unknown'}, expected=400)
group_grant = request('POST', group_path.replace('S-1-', 's-1-'), body=group_command, expected=201)
assert group_grant['subjectKind'] == 'WindowsGroup' and group_grant['userId'] is None
assert group_grant['groupSid'] == group_sid and group_grant['assetIds'] == command['assetIds']
assert request('GET', group_path) == [group_grant]
assert request('GET', '/security/windows-groups/S-1-5-21-710-720-730-741/grants') == []
request('POST', group_path, body=group_command, expected=409)
request('PUT', f"/security/users/{operator['userId']}/grants/{group_grant['grantId']}/deactivate",
        body={'expectedRevision': 1}, expected=404)
request('PUT', f"/security/windows-groups/S-1-5-21-710-720-730-741/grants/{group_grant['grantId']}/deactivate",
        body={'expectedRevision': 1}, expected=404)
retire_group_path = group_path + f"/{group_grant['grantId']}/deactivate"
group_retired = request('PUT', retire_group_path, body={'expectedRevision': 1})
assert not group_retired['isActive'] and group_retired['revision'] == 2
assert group_retired['groupSid'] == group_sid and group_retired['userId'] is None
assert request('GET', group_path) == [group_retired]
request('PUT', retire_group_path, body={'expectedRevision': 1}, expected=409)
assert request('GET', '/assets', OPERATOR) == before_assets
print('Windows-group HTTP authentication, SID normalization, scope validation, subject isolation and retirement passed.')

# Recovery requires an active direct deployment-wide built-in Admin grant.
# Neither the caller's legacy Admin role nor a group grant is a substitute.
admin_role = next(item for item in request('GET', '/security/roles')
                  if item['key'] == 'admin' and item['isSystem'])
recovery_command = {'roleId': admin_role['roleId'], 'scopeKind': 'DeploymentWide',
                    'assetIds': [], 'expectedRoleRevision': admin_role['revision']}
recovery_group = request('POST', group_path, body=recovery_command, expected=201)
recovery_one = request('POST', path, body=recovery_command, expected=201)
recovery_retire = path + f"/{recovery_one['grantId']}/deactivate"
problem = request('PUT', recovery_retire, body={'expectedRevision': 1}, expected=409)
assert problem['code'] == 'SECURITY_LAST_DIRECT_ADMIN_REQUIRED'
assert next(item for item in request('GET', path)
            if item['grantId'] == recovery_one['grantId']) == recovery_one
other_path = f"/security/users/{other['userId']}/grants"
recovery_two = request('POST', other_path, body=recovery_command, expected=201)
retired = request('PUT', recovery_retire, body={'expectedRevision': 1})
assert not retired['isActive'] and retired['revision'] == 2
problem = request('PUT', other_path + f"/{recovery_two['grantId']}/deactivate",
                  body={'expectedRevision': 1}, expected=409)
assert problem['code'] == 'SECURITY_LAST_DIRECT_ADMIN_REQUIRED'
request('PUT', group_path + f"/{recovery_group['grantId']}/deactivate", body={'expectedRevision': 1})
assert request('GET', '/assets', OPERATOR) == before_assets
print('Direct-administrator HTTP recovery refusal, readback, replacement and unchanged runtime authority passed.')
