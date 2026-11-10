"""Provider Simulator -> authenticated Gateway history checks. CygNet has no simulator: without a CygNet build it must report unavailable."""
import json
import os
import urllib.error
import urllib.request

BASE = os.environ.get("WSA_GATEWAY_BASE", "http://127.0.0.1:5081") + "/api/internal/v1/adapters/"

def call(adapter, body, caller="server", status=200, operation="history"):
    headers = {"Content-Type": "application/json"}
    if caller:
        headers["Authorization"] = "WSA-Service " + caller + ":" + os.environ["WSA_" + caller.upper() + "_KEY"]
    request = urllib.request.Request(BASE + adapter + "/" + operation, data=json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            actual, raw = response.status, response.read()
    except urllib.error.HTTPError as error:
        actual, raw = error.code, error.read()
    assert actual == status, (adapter, caller, actual, status, raw[:300])
    return json.loads(raw) if raw else None

read = {"assetExternalId": "101", "quantity": "PumpFillage", "startUtc": "2026-11-01T01:00:00-05:00",
        "endUtc": "2026-11-01T01:00:00-06:00", "maximumSamples": 1}
request = {"connectionId": "88888888-8888-4888-8888-888888888881", "connectionRevision": 1,
           "settings": {"baseUrl": "http://127.0.0.1:5091/foresite"}, "read": read}
for adapter in ("foresite", "cygnet"):
    call(adapter, request, caller=None, status=401)
    call(adapter, request, caller="worker", status=403)
call("unknown", request, status=404)
result = call("foresite", request)
assert result["truncated"] and len(result["samples"]) == 1 and result["samples"][0]["value"] == 82, result
assert result["samples"][0]["quality"] == "Unknown" and result["samples"][0]["unit"] == "%", result
assert result["samples"][0]["timestampUtc"].startswith("2026-11-01T06:00:00"), result
assert result["sourcePoint"] == "SIMULATED-RRLPFILCUR" and result["aggregation"] == "Raw", result
call("foresite", request | {"read": read | {"maximumSamples": 1001}}, status=400)
call("foresite", request | {"read": read | {"endUtc": read["startUtc"]}}, status=400)
call("foresite", request | {"read": read | {"quantity": "NotSupported"}}, status=503)
cygnet = request | {"settings": {"domain": "1", "siteService": "SITE.UIS"},
                    "read": read | {"assetExternalId": "SITE.UIS::WELL_005"}}
call("cygnet", cygnet, status=503)
# Scope and bound checks for CygNet history need a live backend and are covered by the CygNet adapter tests.
print("Gateway bounded history: server/worker/anonymous authorization, explicit provider, scope, UTC/DST, units, quality, truncation and bounds passed (Provider Simulator source; CygNet unavailable without a CygNet build).")

# Service-only configuration contract: no browser preview or discovery operation.
configuration = request | {"read": {"assetExternalId": "101"}}
for caller, status in [(None, 401), ("worker", 403), ("server", 200)]:
    result = call("foresite", configuration, caller=caller, status=status, operation="configuration")
    if status == 200:
        assert result["assetExternalId"] == "101" and result["displayName"] == "Alpha 1", result
        assert result["details"]["classificationCode"] == "1" and result["details"]["facilityId"] == "7", result
        assert result["sourceChangedAtUtc"] is None and result["retrievedAtUtc"], result
        assert "settings" not in result and "quality" not in result, result
call("cygnet", configuration, status=404, operation="configuration")
call("foresite", configuration | {"connectionId": "00000000-0000-0000-0000-000000000000"}, status=503, operation="configuration")
call("foresite", configuration | {"read": {"assetExternalId": "999"}}, status=503, operation="configuration")
print("Gateway configuration: explicit provider, server-only authorization, typed projection and source/retrieval semantics passed (synthetic source).")
