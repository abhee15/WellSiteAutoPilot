"""Replay the POC's paired pressure inputs through the real Gateway and Simulator."""

from datetime import datetime, timedelta, timezone
import json
import os
from urllib.parse import urlencode
from urllib.request import Request, urlopen


SIMULATOR = "http://127.0.0.1:5091/api/sim/v1"
GATEWAY = "http://127.0.0.1:5081/api/internal/v1"


def reset(scenario):
    request = Request(SIMULATOR + "/scenarios/reset", data=json.dumps({"scenario": scenario}).encode(),
                      headers={"Content-Type": "application/json"})
    with urlopen(request, timeout=10) as response:
        assert response.status == 204


def read(well, quantity):
    query = urlencode({"assetExternalId": well, "quantity": quantity})
    request = Request(GATEWAY + "/data/current?" + query,
                      headers={"Authorization": "WSA-Service worker:" + os.environ["WSA_WORKER_KEY"]})
    with urlopen(request, timeout=10) as response:
        value = json.load(response)
    assert value["quantity"] == quantity and value["unit"] == "psi", value
    assert value["source"] == f"simulator:{well}", value
    measured = datetime.fromisoformat(value["timestampUtc"])
    assert measured.utcoffset() == timedelta(0), value
    assert timedelta(0) <= datetime.now(timezone.utc) - measured < timedelta(seconds=120), value
    return value


reset("pressure-differential")
try:
    for well, expected_casing, expected_quality in [
        ("WELL-101", 200, "Good"),
        ("WELL-102", 400, "Good"),
        ("WELL-103", 270, "Good"),
        ("WELL-104", 400, "Bad"),
    ]:
        casing = read(well, "CasingPressure")
        tubing = read(well, "TubingPressure")
        assert (casing["value"], casing["quality"]) == (expected_casing, expected_quality), casing
        assert (tubing["value"], tubing["quality"]) == (120, "Good"), tubing

    with urlopen(SIMULATOR + "/commands/log", timeout=10) as response:
        assert json.load(response)["received"] == 0
finally:
    reset("rod-pump-normal")

print("Paired pressure reads preserved well identity, value, unit, time and quality with zero provider commands.")
