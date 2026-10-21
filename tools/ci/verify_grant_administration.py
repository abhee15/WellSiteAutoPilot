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
# Platform status: detail is for people who administer connections or security; the overall state is for everyone.
request("GET", "/system/info", OPERATOR, expected=403)
request("GET", "/system/info", None, expected=401)
info = request("GET", "/system/info")
assert info["components"] and all(item["name"] and item["version"] for item in info["components"]), info
health = request("GET", "/system/health", OPERATOR)
assert set(health) == {"status", "componentCount", "unhealthyCount"} and health["componentCount"] == len(info["components"]), health
request("GET", "/system/health", None, expected=401)
# Connections: engineers manage them, operators cannot even list them, and the Gateway judges the settings.
request("GET", "/connections", OPERATOR, expected=403)
request("GET", "/connections", None, expected=401)
request("POST", "/connections", OPERATOR, {"name": "Denied", "adapterKey": "simulator", "settings": {}}, expected=403)
adapters = request("GET", "/connections/adapters")
assert [item["key"] for item in adapters] == ["simulator", "foresite", "petrovisor"], adapters
assert all(item["fields"][0]["key"] == "baseUrl" and item["fields"][0]["kind"] == "Url" for item in adapters), adapters
conn_name = "CI simulator " + uuid.uuid4().hex[:8]
request("POST", "/connections", body={"name": conn_name, "adapterKey": "simulator", "settings": {"baseUrl": "ftp://x"}}, expected=400)
request("POST", "/connections", body={"name": conn_name, "adapterKey": "nothing", "settings": {}}, expected=400)
request("POST", "/connections", body={"name": "   ", "adapterKey": "simulator", "settings": {"baseUrl": "http://127.0.0.1:5091"}}, expected=400)
created = request("POST", "/connections", body={"name": conn_name, "adapterKey": "simulator",
    "settings": {"baseUrl": "http://127.0.0.1:5091"}}, expected=201)
assert created["revision"] == 1 and created["isEnabled"] and created["lastTest"] is None, created
request("POST", "/connections", body={"name": conn_name.lower(), "adapterKey": "simulator",
    "settings": {"baseUrl": "http://127.0.0.1:5091"}}, expected=409)
tested = request("POST", "/connections/" + created["id"] + "/test")
assert tested["lastTest"]["outcome"] == "Healthy" and tested["lastTestIsCurrent"], tested
assert tested["lastTest"]["actingAs"], tested
edited = request("PUT", "/connections/" + created["id"], body={"expectedRevision": 1, "name": conn_name,
    "settings": {"baseUrl": "http://127.0.0.1:5091/"}})
assert edited["revision"] == 2 and edited["lastTestIsCurrent"] is False, edited
request("PUT", "/connections/" + created["id"], body={"expectedRevision": 1, "name": conn_name,
    "settings": {"baseUrl": "http://127.0.0.1:5091"}}, expected=409)
off = request("PUT", "/connections/" + created["id"] + "/enabled", body={"expectedRevision": 2, "enabled": False})
assert off["isEnabled"] is False and off["revision"] == 3, off
request("POST", "/connections/" + created["id"] + "/test", expected=409)
request("PUT", "/connections/" + created["id"] + "/enabled", body={"expectedRevision": 3, "enabled": True})
broken = request("POST", "/connections", body={"name": conn_name + " down", "adapterKey": "simulator",
    "settings": {"baseUrl": "http://127.0.0.1:9"}}, expected=201)
assert request("POST", "/connections/" + broken["id"] + "/test")["lastTest"]["outcome"] == "Unreachable"
# ForeSite: the simulator answers at its address but is not ForeSite, and a closed port is unreachable.
not_foresite = request("POST", "/connections", body={"name": conn_name + " not foresite", "adapterKey": "foresite",
    "settings": {"baseUrl": "http://127.0.0.1:5091"}}, expected=201)
checked = request("POST", "/connections/" + not_foresite["id"] + "/test")["lastTest"]
assert checked["outcome"] == "NeedsAttention" and "does not look like ForeSite" in checked["message"], checked
down = request("POST", "/connections", body={"name": conn_name + " foresite down", "adapterKey": "foresite",
    "settings": {"baseUrl": "http://127.0.0.1:9"}}, expected=201)
assert request("POST", "/connections/" + down["id"] + "/test")["lastTest"]["outcome"] == "Unreachable"
# PetroVisor: with no sign-in set up on the Gateway, a test says so and discovery is incomplete, never an empty list.
pv = request("POST", "/connections", body={"name": conn_name + " petrovisor", "adapterKey": "petrovisor",
    "settings": {"baseUrl": "https://petrovisor.invalid/discovery", "workspace": "Production"}}, expected=201)
pv_test = request("POST", "/connections/" + pv["id"] + "/test")["lastTest"]
assert pv_test["outcome"] == "NeedsAttention" and "no PetroVisor sign-in" in pv_test["message"], pv_test
pv_find = request("POST", "/connections/" + pv["id"] + "/discover")
assert pv_find["complete"] is False and pv_find["items"] == [] and pv_find["warnings"], pv_find
request("DELETE", "/connections/" + pv["id"], expected=204)
# Find: the simulator's ForeSite-shaped stand-in is served under /foresite, and discovery reads it as the Gateway's account.
stand_in = request("POST", "/connections", body={"name": conn_name + " foresite stand-in", "adapterKey": "foresite",
    "settings": {"baseUrl": "http://127.0.0.1:5091/foresite"}}, expected=201)
found = request("POST", "/connections/" + stand_in["id"] + "/discover")
assert found["complete"] is True and found["warnings"] == [] and found["actingAs"], found
assert sorted(item["displayName"] for item in found["items"]) == ["Alpha 1", "Alpha 2", "Bravo 1"], found
alpha = next(item for item in found["items"] if item["externalId"] == "102")
assert alpha["kind"] == "foresite.well" and alpha["metadata"]["assetName"] == "North Field", alpha
assert alpha["metadata"]["wellType"] == "ESP" and alpha["metadata"]["deviceType"] == "ESP", alpha
assert alpha["metadata"]["field"] == "Alpha" and alpha["metadata"]["lease"] == "L-1", alpha
# What was found is kept briefly: totals, counts per grouping fact and a sample, then searching and scoping within it.
assert found["totalCount"] == 3 and found["snapshotId"] and found["expiresAtUtc"], found
assert [facet["key"] for facet in found["facets"]] == ["assetName", "wellType", "deviceType", "field", "lease"], found["facets"]
field_facet = next(facet for facet in found["facets"] if facet["key"] == "field")
assert sum(value["count"] for value in field_facet["values"]) == 3 and not field_facet["hasMore"], field_facet
search_url = "/connections/" + stand_in["id"] + "/discover/search"
snapshot = found["snapshotId"]
by_word = request("POST", search_url, body={"snapshotId": snapshot, "query": "alpha"})
assert by_word["matchCount"] == 2 and sorted(item["displayName"] for item in by_word["items"]) == ["Alpha 1", "Alpha 2"], by_word
by_fact = request("POST", search_url, body={"snapshotId": snapshot, "facets": {"assetName": ["South Field"]}})
assert by_fact["matchCount"] == 1 and by_fact["items"][0]["displayName"] == "Bravo 1", by_fact
assert request("POST", search_url, body={"snapshotId": snapshot})["matchCount"] == 3
request("POST", search_url, body={"snapshotId": snapshot, "facets": {"nonsense": ["x"]}}, expected=400)
request("POST", search_url, body={"snapshotId": str(uuid.uuid4())}, expected=409)
request("POST", search_url, OPERATOR, {"snapshotId": snapshot}, expected=403)
# A wrong address is an incomplete answer with a reason, never an empty "complete" list.
lost = request("POST", "/connections/" + not_foresite["id"] + "/discover")
assert lost["complete"] is False and lost["items"] == [] and lost["warnings"], lost
down_find = request("POST", "/connections/" + down["id"] + "/discover")
assert down_find["complete"] is False and down_find["items"] == [], down_find
# Only managers may look, adapters that cannot list say so, and a disabled connection refuses.
request("POST", "/connections/" + stand_in["id"] + "/discover", OPERATOR, expected=403)
request("POST", "/connections/" + created["id"] + "/discover", expected=409)
# Plan: the same discovery grouped as asked, with a fingerprint that identifies exactly this plan. Nothing is saved.
plan_url = "/connections/" + stand_in["id"] + "/import/preview"
preview = request("POST", plan_url, body={"levels": ["assetName", "wellType"]})
assert preview["objectCount"] == 3 and preview["groupCount"] == 5 and preview["fallbackObjectCount"] == 0, preview
assert preview["levels"] == ["assetName", "wellType"] and preview["rootObjectCount"] == 0, preview
assert [(g["name"], g["objectCount"]) for g in preview["groups"]] == [("North Field", 2), ("South Field", 1)], preview
assert [c["name"] for c in preview["groups"][0]["children"]] == ["ESP", "RRL"], preview
assert preview["groups"][1]["children"][0]["sampleNames"] == ["Bravo 1"], preview
assert set(preview["typesToCreate"]) <= {"well", "group"}, preview
assert not (preview["conflicts"] and preview["canImport"]), preview
assert preview["actingAs"], preview
assert request("POST", plan_url, body={"levels": ["assetName", "wellType"]})["fingerprint"] == preview["fingerprint"]
assert request("POST", plan_url, body={"levels": ["assetName"]})["fingerprint"] != preview["fingerprint"]
# The same review can be made of a chosen part of what was found, from the kept list rather than a fresh look.
scoped = request("POST", plan_url, body={"levels": ["assetName"], "snapshotId": snapshot,
    "facets": {"assetName": ["South Field"]}})
assert scoped["objectCount"] == 1 and scoped["groupCount"] == 1 and scoped["groups"][0]["name"] == "South Field", scoped
whole = request("POST", plan_url, body={"levels": ["assetName", "wellType"], "snapshotId": snapshot})
assert whole["fingerprint"] == preview["fingerprint"] and whole["objectCount"] == 3, whole
request("POST", plan_url, body={"levels": [], "snapshotId": str(uuid.uuid4())}, expected=409)
request("POST", "/connections/" + stand_in["id"] + "/import", body={"levels": [], "snapshotId": str(uuid.uuid4()),
    "expectedFingerprint": scoped["fingerprint"]}, expected=409)
flat = request("POST", plan_url, body={})
assert flat["groups"] == [] and flat["rootObjectCount"] == 3 and flat["groupCount"] == 0, flat
request("POST", plan_url, body={"levels": ["nonsense"]}, expected=400)
request("POST", plan_url, body={"levels": ["field", "field"]}, expected=400)
request("POST", plan_url, OPERATOR, {"levels": []}, expected=403)
request("POST", "/connections/" + not_foresite["id"] + "/import/preview", body={"levels": []}, expected=409)
# Import: only the reviewed plan is applied, repeating it adds nothing, and the connection can no longer be deleted.
import_url = "/connections/" + stand_in["id"] + "/import"
levels = ["assetName", "wellType"]
request("POST", import_url, body={"levels": levels, "expectedFingerprint": "not-the-reviewed-plan"}, expected=409)
request("POST", import_url, OPERATOR, {"levels": levels, "expectedFingerprint": preview["fingerprint"]}, expected=403)
imported = request("POST", import_url, body={"levels": levels, "expectedFingerprint": preview["fingerprint"]})
assert imported["wellsCreated"] == 3 and imported["groupsCreated"] + imported["groupsReused"] == 5, imported
assert imported["alreadyImported"] == 0 and set(imported["typesCreated"]) <= {"well", "group"}, imported
names = {item["name"] for item in request("GET", "/assets?limit=500")}
assert {"North Field", "South Field", "ESP", "RRL", "Alpha 1", "Alpha 2", "Bravo 1"} <= names, names
again = request("POST", plan_url, body={"levels": levels})
assert again["objectCount"] == 0 and again["alreadyImported"] == 3 and again["groupCount"] == 0, again
repeat = request("POST", import_url, body={"levels": levels, "expectedFingerprint": again["fingerprint"]})
assert repeat["wellsCreated"] == 0 and repeat["groupsCreated"] == 0 and repeat["alreadyImported"] == 3, repeat
# Once Assets were imported the connection cannot be pointed at another system, but can still be renamed.
current = request("GET", "/connections/" + stand_in["id"])
request("PUT", "/connections/" + stand_in["id"], body={"expectedRevision": current["revision"], "name": current["name"],
    "settings": {"baseUrl": "http://127.0.0.1:5091/other"}}, expected=409)
renamed = request("PUT", "/connections/" + stand_in["id"], body={"expectedRevision": current["revision"],
    "name": conn_name + " renamed stand-in", "settings": {"baseUrl": "http://127.0.0.1:5091/foresite"}})
assert renamed["revision"] == current["revision"] + 1 and renamed["name"].endswith("renamed stand-in"), renamed
# Ownership: what a connection imported belongs to it alone. A second connection to the same system gets its own
# Assets and its own Groups, even when every name is the same; nothing is shared or merged.
before = request("GET", "/assets?limit=500")
mine = {item["id"]: item for item in before if item.get("ownerConnectionId") == stand_in["id"]}
assert {item["name"] for item in mine.values()} >= {"North Field", "South Field", "ESP", "RRL", "Alpha 1", "Alpha 2", "Bravo 1"}, mine
assert all(item.get("ownerConnectionId") for item in mine.values()), mine
twin = request("POST", "/connections", body={"name": conn_name + " foresite twin", "adapterKey": "foresite",
    "settings": {"baseUrl": "http://127.0.0.1:5091/foresite"}}, expected=201)
twin_plan = request("POST", "/connections/" + twin["id"] + "/import/preview", body={"levels": levels})
assert twin_plan["objectCount"] == 3 and twin_plan["alreadyImported"] == 0, twin_plan
twin_done = request("POST", "/connections/" + twin["id"] + "/import", body={"levels": levels, "expectedFingerprint": twin_plan["fingerprint"]})
assert twin_done["wellsCreated"] == 3 and twin_done["groupsCreated"] == 5 and twin_done["groupsReused"] == 0, twin_done
after = request("GET", "/assets?limit=500")
for name in ["Alpha 1", "North Field", "ESP"]:
    owners = sorted(item["ownerConnectionId"] for item in after if item["name"] == name and item.get("ownerConnectionId"))
    assert owners == sorted([stand_in["id"], twin["id"]]), (name, owners)
assert {item["id"] for item in after if item.get("ownerConnectionId") == stand_in["id"]} == set(mine), "the first connection's Assets changed"
request("DELETE", "/connections/" + twin["id"], expected=409)
# Creating beneath a connection's Group keeps that owner; a root Asset stays standalone.
well_type = next(item for item in request("GET", "/asset-types") if item["key"] == "well")
north = next(item for item in after if item["name"] == "North Field" and item.get("ownerConnectionId") == stand_in["id"])
inside = request("POST", "/assets", body={"assetTypeId": well_type["id"], "name": "Added by hand", "parentAssetId": north["id"],
    "attributeValuesJson": "{}"}, expected=201)
assert inside["ownerConnectionId"] == stand_in["id"], inside
alone = request("POST", "/assets", body={"assetTypeId": well_type["id"], "name": "Standalone by hand", "parentAssetId": None,
    "attributeValuesJson": "{}"}, expected=201)
assert alone.get("ownerConnectionId") is None, alone
# The top level of the hierarchy is the owners: each connection (with its name) and the standalone Assets.
owners = request("GET", "/assets/owners")
by_owner = {item["connectionId"]: item for item in owners}
assert stand_in["id"] in by_owner and twin["id"] in by_owner and None in by_owner, owners
assert by_owner[stand_in["id"]]["connectionName"].endswith("renamed stand-in") and by_owner[twin["id"]]["connectionName"].endswith("foresite twin"), owners
assert by_owner[twin["id"]]["topLevelCount"] == 2 and by_owner[twin["id"]]["assetCount"] == 8, by_owner[twin["id"]]
assert by_owner[None]["connectionName"] is None and by_owner[None]["topLevelCount"] >= 1, by_owner[None]
assert owners[-1]["connectionId"] is None, "standalone is listed last"
tops = request("GET", "/assets/children?ownerConnectionId=" + twin["id"])
assert sorted(item["name"] for item in tops) == ["North Field", "South Field"] and all(item["ownerConnectionId"] == twin["id"] for item in tops), tops
alone_tops = request("GET", "/assets/children?standalone=true")
assert "Standalone by hand" in {item["name"] for item in alone_tops} and not any(item["ownerConnectionId"] for item in alone_tops), alone_tops
request("GET", "/assets/children?ownerConnectionId=" + twin["id"] + "&standalone=true", expected=400)
assert request("GET", "/assets/children?ownerConnectionId=" + str(uuid.uuid4())) == []
# A person without asset read access is not told which connections exist.
request("GET", "/assets/owners", OPERATOR, expected=403)
request("DELETE", "/connections/" + stand_in["id"], expected=409)
request("DELETE", "/connections/" + not_foresite["id"], expected=204)
request("DELETE", "/connections/" + down["id"], expected=204)
assert {item["id"] for item in request("GET", "/connections")} >= {created["id"], broken["id"]}
request("DELETE", "/connections/" + broken["id"], expected=204)
request("DELETE", "/connections/" + created["id"], expected=204)
request("GET", "/connections/" + created["id"], expected=404)
audit = request("GET", "/security/audit-events?limit=100&actionPrefix=connection.")["items"]
actions = {item["action"] for item in audit}
assert {"connection.created", "connection.updated", "connection.tested", "connection.discovered", "connection.imported", "connection.deleted"} <= actions, actions
assert not any("127.0.0.1:5091" in json.dumps(item) for item in audit if item["action"].startswith("connection.")), "settings values must not be audited"
print("Live grant HTTP matrix passed: paired scopes, no descendant inheritance, global/resource separation, protected administration, untrusted-group denial and fresh revocation.")
