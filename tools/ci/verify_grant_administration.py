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
