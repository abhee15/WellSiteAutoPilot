"""Verify the live paired-grant HTTP boundary against the CI Server/PostgreSQL."""
import json
import sys
import urllib.error
import urllib.request
import uuid

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
ADMIN = r"CI\wsa-admin"
OPERATOR = r"CI\operator-one"
OTHER = r"CI\grant-other"
GROUP_ONLY = r"CI\group-only"


def request(method, path, identity=ADMIN, body=None, expected=200, extra_headers=None):
    headers = {"X-Correlation-ID": "ci-grant-runtime"}
    if identity is not None:
        headers["X-WSA-Test-Identity"] = identity
    if body is not None:
        headers["Content-Type"] = "application/json"
    headers.update(extra_headers or {})
    message = urllib.request.Request(BASE_URL + "/api/v1" + path,
        data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        response = urllib.request.urlopen(message, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        payload = response.read().decode()
        if response.code != expected:
            raise AssertionError(f"{method} {path}: expected {expected}, got {response.code}: {payload}")
        return json.loads(payload) if payload else None


def role(name, permissions):
    return request("POST", "/security/roles", body={"name": name + " " + uuid.uuid4().hex[:10],
        "description": "Paired runtime HTTP fixture", "permissions": permissions}, expected=201)


def grant(user, definition, scope, assets=(), group=False, expected=201):
    path = "/security/windows-groups/" + user if group else "/security/users/" + user
    return request("POST", path + "/grants", body={"roleId": definition["roleId"],
        "expectedRoleRevision": definition["revision"], "scopeKind": scope, "assetIds": list(assets)}, expected=expected)


def retire(user, assignment, group=False):
    path = "/security/windows-groups/" + user if group else "/security/users/" + user
    return request("PUT", path + "/grants/" + assignment["grantId"] + "/deactivate",
        body={"expectedRevision": assignment["revision"]})


admin = request("GET", "/security/me")
operator = request("GET", "/security/me", OPERATOR)
other = request("GET", "/security/me", OTHER)
group_only = request("GET", "/security/me", GROUP_ONLY)
assert "security.manage" in admin["permissions"]
assert operator["permissions"] == other["permissions"] == group_only["permissions"] == []
assert all("roles" not in session and "assetScopeIds" not in session
           for session in [admin, operator, other, group_only])
for identity, status in [(None, 401), (OPERATOR, 403)]:
    for path in ["/assets", "/executions", "/configured-logic", "/recommendations", "/logic-modules",
                 "/security/users", "/security/roles", "/security/role-permissions", "/security/assets/directory"]:
        request("GET", path, identity, expected=status)

asset_type = request("POST", "/asset-types", body={"key": "ci-runtime-" + uuid.uuid4().hex[:10],
    "displayName": "Grant runtime well", "attributeSchemaJson": '{"type":"object"}'}, expected=201)


def asset(name, parent=None, identity=ADMIN, expected=201):
    return request("POST", "/assets", identity, {"assetTypeId": asset_type["id"], "name": name,
        "parentAssetId": parent, "attributeValuesJson": "{}"}, expected=expected)


first = asset("Runtime A")
second = asset("Runtime B")
outside = asset("Runtime C")
unused_type = request("POST", "/asset-types", body={"key": "unused-runtime-" + uuid.uuid4().hex[:10],
    "displayName": "Unassigned type", "attributeSchemaJson": '{"type":"object"}'}, expected=201)
reader = role("Reader", ["assets.read", "configured-logic.read", "executions.read", "recommendations.read"])
writer = role("Manager", ["assets.manage", "configured-logic.manage", "executions.request", "recommendations.decide"])
read_grant = grant(operator["userId"], reader, "ExactAssets", [first["id"], second["id"]])
write_grant = grant(operator["userId"], writer, "ExactAssets", [first["id"]])
current = request("GET", "/security/me", OPERATOR)
assert set(reader["permissions"] + writer["permissions"]) <= set(current["permissions"])
assert "security.manage" not in current["permissions"]

assert {item["id"] for item in request("GET", "/asset-types", OPERATOR)} == {asset_type["id"]}
request("GET", "/asset-types/" + unused_type["id"], OPERATOR, expected=404)
assert unused_type["id"] in {item["id"] for item in request("GET", "/asset-types")}
visible = request("GET", "/assets", OPERATOR)
assert {item["id"] for item in visible} == {first["id"], second["id"]}
request("GET", "/assets/" + outside["id"], OPERATOR, expected=404)
assert len(request("GET", "/assets?limit=1", OPERATOR)) == 1
request("GET", "/assets?limit=0", OPERATOR, expected=400)
request("GET", "/executions?status=Unrecognized", OPERATOR, expected=400)
request("GET", "/recommendations?limit=101", OPERATOR, expected=400)
capabilities = request("GET", "/assets/capabilities", OPERATOR)
assert capabilities == {"canCreateRoot": False, "canCreateUnderAuthorizedParents": True}
asset("Denied root", identity=OPERATOR, expected=403)
asset("Denied B child", second["id"], OPERATOR, expected=403)
child = asset("Permitted A child", first["id"], OPERATOR)
request("GET", "/assets/" + child["id"], OPERATOR, expected=404)
assert {item["id"] for item in request("GET", "/assets", OPERATOR)} == {first["id"], second["id"]}
# Protected assignment-directory metadata is separate from resource access.
request("GET", "/security/assets/directory", OPERATOR, expected=403)
assert {first["id"], second["id"], outside["id"], child["id"]} <= {
    item["id"] for item in request("GET", "/security/assets/directory?limit=500")}

# Exact/all-Asset permission strings cannot satisfy a deployment-wide operation.
catalog = role("Catalog", ["logic.read", "assets.read"])
resource_catalog = grant(operator["userId"], catalog, "ExactAssets", [first["id"]])
request("GET", "/logic-modules", OPERATOR, expected=403)
global_catalog = grant(operator["userId"], catalog, "DeploymentWide")
request("GET", "/logic-modules", OPERATOR)
retire(operator["userId"], global_catalog)
request("GET", "/logic-modules", OPERATOR, expected=403)
retire(operator["userId"], resource_catalog)

# A custom role named like an administrator, or carrying security.manage,
# cannot cross the protected system-administrator boundary.
custom_admin = role("Customer Admin", ["security.manage"])
custom_assignment = grant(other["userId"], custom_admin, "DeploymentWide")
assert "security.manage" not in request("GET", "/security/me", OTHER)["permissions"]
request("GET", "/security/users", OTHER, expected=403)
request("POST", "/security/roles", OTHER, {"name": "Denied", "description": "", "permissions": []}, expected=403)
retire(other["userId"], custom_assignment)

# Group assignment authoring remains supported. TestHeader must not supply
# native Windows membership or authorize a simulated group-only principal.
sid = "S-1-5-21-771-992-553-4001"
system_admin = next(item for item in request("GET", "/security/roles?limit=500") if item["key"] == "admin")
group_admin = grant(sid, system_admin, "DeploymentWide", group=True)
group_read = grant(sid, reader, "ExactAssets", [second["id"]], group=True)
spoofed = {"X-WSA-Test-Groups": sid, "X-WSA-Test-Roles": "Admin"}
assert request("GET", "/security/me", GROUP_ONLY, extra_headers=spoofed)["permissions"] == []
request("GET", "/assets", GROUP_ONLY, expected=403, extra_headers=spoofed)
request("GET", "/security/users", GROUP_ONLY, expected=403, extra_headers=spoofed)
assert len(request("GET", "/security/windows-groups/" + sid + "/grants")) == 2
retire(sid, group_admin, group=True)
retire(sid, group_read, group=True)

# Administration refuses self elevation, invalid scopes and stale role reviews.
grant(admin["userId"], reader, "ExactAssets", [first["id"]], expected=403)
for scope in ["Unknown", "0", "1"]:
    grant(other["userId"], reader, scope, [first["id"]], expected=400)
grant(other["userId"], reader, "ExactAssets", expected=400)
stale = role("Stale review", ["assets.read"])
request("PUT", "/security/roles/" + stale["roleId"], body={"expectedRevision": stale["revision"],
    "name": stale["name"], "description": "Changed review", "permissions": stale["permissions"], "isActive": True})
grant(other["userId"], stale, "ExactAssets", [first["id"]], expected=409)
retire(operator["userId"], write_grant)
assert "assets.manage" not in request("GET", "/security/me", OPERATOR)["permissions"]
asset("Revoked write", first["id"], OPERATOR, expected=403)
assert {item["id"] for item in request("GET", "/assets", OPERATOR)} == {first["id"], second["id"]}
retire(operator["userId"], read_grant)
request("GET", "/assets", OPERATOR, expected=403)
assert request("GET", "/security/me", OPERATOR)["permissions"] == []
print("Live grant HTTP matrix passed: paired scopes, no descendant inheritance, global/resource separation, protected administration, untrusted-group denial and fresh revocation.")
