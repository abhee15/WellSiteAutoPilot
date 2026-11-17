"""Provider Simulator -> authenticated Gateway history checks. CygNet has no simulator: without a CygNet build it must report unavailable."""
import json
import os
from datetime import datetime
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
registered_path = "reads/history.pump-fillage"
registered = {key: request[key] for key in ("connectionId", "connectionRevision", "settings")}
registered |= {"resourceKind": "foresite.well", "externalId": "101", "contractVersion": "1.0", "input": read}
registered_result = call("foresite", registered, operation=registered_path)
for response in (result, registered_result):
    for sample in response["samples"]:
        assert sample.pop("retrievedAtUtc")
assert registered_result == result, registered_result
call("foresite", registered, caller=None, status=401, operation=registered_path)
call("foresite", registered, caller="worker", status=403, operation=registered_path)
call("petrovisor", registered, status=404, operation=registered_path)
call("foresite", registered | {"externalId": "999"}, status=400, operation=registered_path)
call("foresite", registered | {"input": read | {"quantity": "NotSupported"}}, status=400, operation=registered_path)
call("foresite", request | {"read": read | {"maximumSamples": 1001}}, status=400)
call("foresite", request | {"read": read | {"endUtc": read["startUtc"]}}, status=400)
call("foresite", request | {"read": read | {"quantity": "NotSupported"}}, status=503)
stroke_rate = registered | {"input": read | {"quantity": "SPM", "maximumSamples": 2}}
stroke_path = "reads/history.stroke-rate"
call("foresite", stroke_rate, caller="worker", status=403, operation=stroke_path)
call("foresite", registered, status=400, operation=stroke_path)
call("foresite", stroke_rate, status=400, operation=registered_path)
stroke_series = call("foresite", stroke_rate, operation=stroke_path)
assert stroke_series["quantity"] == "SPM" and stroke_series["sourcePoint"] == "SIMULATED-SPSTRN", stroke_series
assert [(x["value"], x["unit"], x["quality"]) for x in stroke_series["samples"]] == [
    (10.3, "SPM", "Unknown"), (11.4, "SPM", "Unknown")], stroke_series
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

# The latest reported test is read independently from history and eligibility.
test_path = "reads/well-test.latest-reported"
def latest_test(well):
    return registered | {"externalId": well, "input": {"assetExternalId": well}}

call("foresite", latest_test("101"), caller=None, status=401, operation=test_path)
call("foresite", latest_test("101"), caller="worker", status=403, operation=test_path)
call("petrovisor", latest_test("101"), status=404, operation=test_path)
call("cygnet", latest_test("101"), status=404, operation=test_path)
call("foresite", latest_test("101") | {"externalId": "102"}, status=400, operation=test_path)
rrl = call("foresite", latest_test("101"), operation=test_path)
assert rrl["assetExternalId"] == "101" and rrl["test"]["id"] == "8101", rrl
assert rrl["test"]["status"] is None and rrl["test"]["validation"]["classificationCode"] is None, rrl
review = call("foresite", latest_test("102"), operation=test_path)
assert review["test"]["status"] == "Review", review
assert review["test"]["validation"]["classificationDescription"] == "RejectedTest", review
assert review["test"]["validation"]["description"] == "Oil rate above acceptance limit", review
assert review["test"]["reportedQuantityFields"] == ["Oil", "Water"] and "oil" not in review["test"], review
assert review["test"]["sampleDate"].endswith("-06:00") and review["test"]["testEndDate"].endswith("-06:00"), review
assert datetime.fromisoformat(review["retrievedAtUtc"]) > datetime.fromisoformat(review["test"]["testEndDate"]), review
assert call("foresite", latest_test("103"), operation=test_path)["test"] is None

def reset_simulator(scenario):
    url = os.environ.get("WSA_SIMULATOR_BASE", "http://127.0.0.1:5091") + "/api/sim/v1/scenarios/reset"
    request = urllib.request.Request(url, data=json.dumps({"scenario": scenario}).encode(),
                                     headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=10) as response:
        assert response.status == 204, response.status

reset_simulator("foresite-well-test-wrong-well")
try:
    error = call("foresite", latest_test("101"), status=503, operation=test_path)
    assert error["code"] == "FORESITE_WELL_TEST_IDENTITY_MISMATCH" and "test" not in error, error
finally:
    reset_simulator("rod-pump-normal")
print("Gateway latest reported well test: Server-only, optional capability, request and provider identity, separate status and validation, and no-record paths passed (synthetic source).")


# Production-day aggregates use a separate operation from raw history.
daily_path = "reads/daily.inferred-total-fluid-rate"
daily_read = {"assetExternalId": "101", "startUtc": "2026-10-31T19:00:00-05:00",
              "endUtc": "2026-11-03T00:00:00-06:00"}
daily = registered | {"input": daily_read}
call("foresite", daily, caller=None, status=401, operation=daily_path)
call("foresite", daily, caller="worker", status=403, operation=daily_path)
call("cygnet", daily, status=404, operation=daily_path)
call("foresite", daily | {"externalId": "102"}, status=400, operation=daily_path)
call("foresite", daily | {"contractVersion": "2.0"}, status=409, operation=daily_path)
for invalid in ({"startUtc": "2026-11-01T00:00:00"}, {"startUtc": "2026-11-01T00:00:00.001Z"},
                {"startUtc": "2026-09-30T00:00:00Z"}):
    call("foresite", daily | {"input": daily_read | invalid}, status=400, operation=daily_path)
daily_result = call("foresite", daily, operation=daily_path)
assert daily_result["source"] == "foresite:daily-averages" and daily_result["unit"] == "STB/d", daily_result
assert daily_result["quantity"] == "TotalFluidRateInferred" and daily_result["assetExternalId"] == "101", daily_result
assert [d["id"] for d in daily_result["days"]] == ["7001", "7002"], daily_result
first_day, second_day = daily_result["days"]
assert first_day["value"] == 0 and second_day["value"] is None, daily_result
assert second_day["start"].endswith("-06:00") and second_day["end"].endswith("-06:00"), daily_result
duration = datetime.fromisoformat(first_day["end"]) - datetime.fromisoformat(first_day["start"])
assert duration.total_seconds() == 89999.99, duration
assert datetime.fromisoformat(daily_result["retrievedAtUtc"]) > datetime.fromisoformat(first_day["end"]), daily_result

def daily_for(well):
    return daily | {"externalId": well, "input": daily_read | {"assetExternalId": well}}

assert call("foresite", daily_for("103"), operation=daily_path)["days"] == []
failure = call("foresite", daily_for("102"), status=503, operation=daily_path)
assert failure["code"] == "FORESITE_DAILY_WELL_MISMATCH", failure
print("Gateway daily averages: recorded DST boundary shape, scope, exact offset handling, empty, zero and null passed (Provider Simulator).")


# The saved-connection card read preserves type, pairs, capture and retrieval evidence.
cards = {"connectionId": registered["connectionId"], "connectionRevision": registered["connectionRevision"],
         "settings": registered["settings"], "externalId": "101", "quantity": "SurfaceCards"}
call("foresite", cards, caller=None, status=401, operation="current")
call("foresite", cards, caller="worker", status=403, operation="current")
card_value = call("foresite", cards, operation="current")
assert card_value["value"] is None and card_value["quality"] == "Unknown", card_value
assert card_value["timestampKind"] == "CardCapture" and card_value["retrievedAtUtc"], card_value
assert card_value["structuredValue"]["schemaId"] == "foresite.surface-cards/v1", card_value
surface_cards = card_value["structuredValue"]["data"]
assert [card["kind"] for card in surface_cards] == ["CURRENT", "FULL"], surface_cards
assert all(card["position"] == [0, 5, 12, 10, 2, 0] and len(card["load"]) == 6 for card in surface_cards)
assert surface_cards[0]["load"] != surface_cards[1]["load"], surface_cards
assert all(card["positionUnit"] == "in" and card["loadUnit"] == "lb" for card in surface_cards)
assert min(datetime.fromisoformat(card["capturedAtUtc"]) for card in surface_cards) == (
    datetime.fromisoformat(card_value["timestampUtc"])), card_value
missing_cards = call("foresite", cards | {"externalId": "102"}, status=503, operation="current")
assert missing_cards["code"] == "FORESITE_MEASUREMENT_MISSING", missing_cards
print("Gateway stored surface cards: Server-only, type, paired series, units, capture, Unknown quality and empty response passed (Provider Simulator).")


# ForeSite returns current alarm records for a well, including suppressed ones.
alarms_path = "reads/alarms.active-well"
def alarms_for(well):
    return registered | {"externalId": well, "input": {"assetExternalId": well}}

call("foresite", alarms_for("101"), caller=None, status=401, operation=alarms_path)
call("foresite", alarms_for("101"), caller="worker", status=403, operation=alarms_path)
call("cygnet", alarms_for("101"), status=404, operation=alarms_path)
call("foresite", alarms_for("101") | {"contractVersion": "2.0"}, status=409, operation=alarms_path)
call("foresite", alarms_for("101") | {"externalId": "102"}, status=400, operation=alarms_path)
call("foresite", alarms_for("bad-id"), status=400, operation=alarms_path)
result = call("foresite", alarms_for("101"), operation=alarms_path)
assert result["assetExternalId"] == "101" and result["source"] == "foresite:current-alarms", result
first, second = result["alarms"]
assert first["id"] == "9001" and first["typeId"] == 1 and first["name"] == "High Rod Stress", first
assert first["numericValue"] == 0 and first["stringValue"] is None, first
assert first["isDeviation"] is False and first["isManuallySuppressed"] is None, first
assert first["startedAt"].endswith("-05:00") and datetime.fromisoformat(first["startedAt"]).year == 2023, first
assert datetime.fromisoformat(result["retrievedAtUtc"]) > datetime.fromisoformat(first["startedAt"]), result
assert second["id"] == "9002" and second["numericValue"] is None and second["stringValue"] == "Run Time 7 days (98)", second
assert second["isDeviation"] is True and second["isManuallySuppressed"] is True, second
assert call("foresite", alarms_for("103"), operation=alarms_path)["alarms"] == []
for well, code in (("102", "FORESITE_ALARM_WELL_MISMATCH"), ("104", "FORESITE_ALARM_ROW_LIMIT"),
                   ("105", "FORESITE_ALARM_RESPONSE_INVALID")):
    error = call("foresite", alarms_for(well), status=503, operation=alarms_path)
    assert error["code"] == code and "alarms" not in error, error
print("Gateway ForeSite active alarms: scoped identity, authorization, source time, flag/value semantics and bounds passed (Provider Simulator).")


# Saved values are read through the passive provider route and retain their representation.
saved_path = "reads/configuration.saved-setpoints"
def setpoints_for(well):
    return registered | {"externalId": well, "input": {"assetExternalId": well}}

call("foresite", setpoints_for("101"), caller=None, status=401, operation=saved_path)
call("foresite", setpoints_for("101"), caller="worker", status=403, operation=saved_path)
call("cygnet", setpoints_for("101"), status=404, operation=saved_path)
call("foresite", setpoints_for("101") | {"externalId": "102"}, status=400, operation=saved_path)
call("foresite", setpoints_for("101") | {"contractVersion": "2.0"}, status=409, operation=saved_path)
call("foresite", setpoints_for("bad-id"), status=400, operation=saved_path)
call("foresite", setpoints_for("101") | {"input": {"assetExternalId": "101", "scanOption": 1}},
     status=400, operation=saved_path)
result = call("foresite", setpoints_for("101"), operation=saved_path)
assert result["assetExternalId"] == "101" and result["source"] == "foresite:saved-setpoints", result
group, = result["groups"]
assert group["type"] == "SetPoints" and group["deviceType"] == "WellPilotRPOC" and group["isWritable"] is True, group
assert group["ordinal"] is None and group["transactionTime"].endswith("-05:00"), group
assert datetime.fromisoformat(result["retrievedAtUtc"]) > datetime.fromisoformat(group["transactionTime"]), result
element, = group["setpoints"]
assert element["elementId"] == "IdleTime" and element["displayValue"] == "00:01:00" and element["originalValue"] == "60", element
assert element["unit"] == element["originalUnit"] == "s" and element["isReadOnly"] is False, element
assert (element["rangeLow"], element["rangeHigh"]) == (0, 4294967295), element
assert call("foresite", setpoints_for("103"), operation=saved_path)["groups"] == []
for well, code in (("102", "FORESITE_SETPOINT_WELL_MISMATCH"), ("104", "FORESITE_SETPOINT_GROUP_FAILED"),
                   ("105", "FORESITE_SETPOINT_ROW_LIMIT")):
    error = call("foresite", setpoints_for(well), status=503, operation=saved_path)
    assert error["code"] == code and "groups" not in error, error
print("Gateway ForeSite saved setpoints: passive route, exact scope, group timestamps, original values, hidden elements and bounds passed (Provider Simulator).")
