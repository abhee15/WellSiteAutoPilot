"""Synthetic provider -> authenticated Gateway history checks, never live provider validation."""
import json
import os
import urllib.error
import urllib.request

BASE = os.environ.get("WSA_GATEWAY_BASE", "http://127.0.0.1:5081") + "/api/internal/v1/adapters/"

def call(adapter, body, caller="server", status=200):
    headers = {"Content-Type": "application/json"}
    if caller:
        headers["Authorization"] = "WSA-Service " + caller + ":" + os.environ["WSA_" + caller.upper() + "_KEY"]
    request = urllib.request.Request(BASE + adapter + "/history", data=json.dumps(body).encode(), headers=headers)
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
result = call("cygnet", cygnet)
assert result["truncated"] and result["source"].startswith("cygnet-simulated"), result
assert result["samples"][0]["quality"] == "Uncertain", result
call("cygnet", cygnet | {"read": cygnet["read"] | {"assetExternalId": "OTHER.UIS::WELL_005"}}, status=400)
call("cygnet", cygnet | {"read": cygnet["read"] | {"maximumSamples": 0}}, status=400)
print("Gateway bounded history: server/worker/anonymous authorization, explicit provider, scope, UTC/DST, units, quality, truncation and bounds passed (simulated sources).")
