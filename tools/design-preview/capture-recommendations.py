"""Review Recommendations task organization and scroll ownership against local mock previews.

Usage: python capture-recommendations.py OUTPUT BEFORE_URL AFTER_URL
No provider or real command endpoint is used. Shared-form capture covers delayed writes/refusals.
"""
import json
import os
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from playwright.sync_api import sync_playwright

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
report = []
identity = lambda number: f"{number if number >= 700 else 0:08x}-0000-4000-8000-{number:012d}"
now = datetime.now(timezone.utc)
records = [{
    "id": identity(700 + number), "assetId": identity(11 + number % 2),
    "executionId": identity(101 + number % 3), "configuredLogicId": identity(500),
    "configurationRevisionId": identity(300), "moduleId": "esp-frequency-optimizer", "moduleVersion": "1.2.0",
    "status": "Approved" if number >= 12 else "Created", "canDecide": True,
    "command": "Set pump speed", "suggestedValue": 48 + number, "unit": "Hz", "quantity": "PumpSpeed",
    "reasonCode": "pump_efficiency_low", "createdAtUtc": (now - timedelta(minutes=number)).isoformat(),
    "expiresAtUtc": (now + timedelta(hours=-1 if 9 <= number < 12 else 1)).isoformat(),
    "decisionBy": "FIELD\\reviewer" if number >= 12 else None,
    "decisionAtUtc": now.isoformat() if number >= 12 else None,
} for number in range(15)]
actions = [{
    "id": identity(800 + number), "recommendationId": identity(712), "assetId": identity(11 + number % 2), "executionId": identity(101 + number % 3),
    "command": "SET_CHOKE", "value": 40 + number, "unit": "%", "status": "Ambiguous",
    "gateCode": "SINK_AMBIGUOUS", "createdAtUtc": (now - timedelta(hours=number + 1)).isoformat(), "canManage": True,
} for number in range(4)]

with sync_playwright() as playwright:
    options = {"headless": True}
    if os.environ.get("CHROMIUM_PATH"):
        options.update(executable_path=os.environ["CHROMIUM_PATH"], args=["--no-sandbox"])
    browser = playwright.chromium.launch(**options)
    for revision, base in zip(("before", "after"), sys.argv[2:4]):
        parsed = urlparse(base)
        assert parsed.scheme == "http" and parsed.hostname in ("127.0.0.1", "localhost"), "Use local mocks only"
        for width, height in ((1440, 900), (900, 820), (420, 850), (320, 850), (1440, 500)):
            for state in ("queue", "decision", "expired", "approved", "read-only", "commands", "settings"):
                if revision == "before" and state == "settings":
                    continue  # This task address is new; the old panel is captured on its queue.
                page = browser.new_page(viewport={"width": width, "height": height}, has_touch=width <= 420,
                                        timezone_id="America/Chicago", reduced_motion="reduce")
                page.context.add_cookies([{"name": "persona", "value": "Admin", "url": base}])
                def recommendations(route):
                    assert route.request.method == "GET", "This audit must never send a command"
                    path = urlparse(route.request.url)
                    query = parse_qs(path.query)
                    if path.path.endswith(identity(700)):
                        route.fulfill(json=records[0])
                    else:
                        run = query.get("executionId", [None])[0]
                        route.fulfill(json=[record for record in records if not run or record["executionId"] == run])
                page.route(re.compile(r"/api/v1/recommendations(?:[/?]|$)"), recommendations)
                page.route(re.compile(r"/api/v1/control/actions\?"), lambda route: route.fulfill(json=actions))
                if state == "read-only":
                    def read_only(route):
                        user = route.fetch().json()
                        user["permissions"] = [permission for permission in user["permissions"]
                                               if permission not in ("control.manage", "recommendations.decide")]
                        route.fulfill(json=user)
                    page.route("**/api/v1/security/me", read_only)
                fragment = "#recommendations/" + identity(700) if state == "decision" else (
                    "#recommendations/actions" if state == "commands" else (
                        "#recommendations/settings" if state == "settings" else "#recommendations"))
                page.goto(base + "/" + fragment)
                if state == "decision":
                    page.get_by_role("region", name="Recommendation decision review", exact=True).wait_for()
                elif state == "settings":
                    page.get_by_role("button", name="Engage kill switch", exact=True).wait_for()
                elif state == "commands":
                    page.get_by_role("button", name=re.compile("Verify.*SET_CHOKE")).first.wait_for()
                else:
                    page.get_by_text("Set pump speed", exact=True).first.wait_for()
                if state in ("expired", "approved"):
                    label = "Expired" if state == "expired" else "Decided"
                    old_label = "Expired or invalid expiry" if state == "expired" else "Approved or rejected"
                    page.locator('.summary-card').filter(has_text=label if revision == "after" else old_label).click()
                    page.get_by_text("Set pump speed", exact=True).first.wait_for()
                page.evaluate("document.fonts.ready")
                audit = page.evaluate("""() => {
                    const visible = e => e.getClientRects().length;
                    return {documentWidth:document.documentElement.scrollWidth, viewportWidth:innerWidth,
                        documentHeight:document.documentElement.scrollHeight, viewportHeight:innerHeight,
                        regions:[...document.querySelectorAll('.page-body,.recommendations-control-context,.split-panes,.split-browse,.split-selected')]
                        .filter(visible).map(e => ({name:e.className, top:e.getBoundingClientRect().top,
                            bottom:e.getBoundingClientRect().bottom, height:e.clientHeight, scroll:e.scrollHeight,
                            overflow:getComputedStyle(e).overflowY}))};
                }""")
                if revision == "after":
                    assert audit["documentWidth"] <= width, (state, width, "horizontal overflow", audit)
                    if width > 900 and height > 576:
                        outer = next(region for region in audit["regions"] if region["name"] == "page-body")
                        assert outer["scroll"] <= outer["height"] + 1, (state, width, "outer scroll", audit)
                        assert audit["documentHeight"] <= height + 1, (state, width, "document scroll", audit)
                        if state in ("decision", "queue", "expired", "approved", "read-only"):
                            panes = next(region for region in audit["regions"] if region["name"] == "split-panes")
                            assert panes["bottom"] <= height and panes["height"] > 150, (state, audit)
                    if state == "read-only":
                        assert page.get_by_role("button", name="Review", exact=True).count() == 0
                        assert page.get_by_role("button", name="Execute command", exact=True).count() == 0
                    if state == "commands":
                        page.locator('.control-actions-context a[href="#assets/' + identity(11) + '"]').first.wait_for()
                        assert page.locator('.control-actions-context').count() == len(actions)
                        assert page.locator('.control-actions-context a[href^="#operations/"]').count() == len(actions)
                    if state == "decision":
                        detail = page.locator('.split-selected')
                        if width <= 900:
                            assert detail.evaluate("element => element === document.activeElement"), (width, "missing detail focus")
                        assert not page.locator('.decision-metadata').evaluate("element => element.open")
                        page.get_by_text("Proposal details and timestamps", exact=True).click()
                        assert page.locator('.decision-metadata').evaluate("element => element.open")
                        page.get_by_text("Proposal details and timestamps", exact=True).click()
                    if state == "queue":
                        assert page.get_by_role("button", name="Engage kill switch", exact=True).count() == 0
                        assert page.get_by_role("button", name=re.compile("Verify.*SET_CHOKE")).count() == 0
                        assert page.get_by_role("link", name="Review unconfirmed commands", exact=True).is_visible()
                page.screenshot(path=str(output / f"{revision}-{width}-{height}-{state}.png"))
                if revision == "after" and state == "queue":
                    # Open/Back retains a real keyboard destination; desktop list scroll is independent of review.
                    trigger = page.get_by_role("button", name="Review", exact=True).first
                    trigger.focus()
                    trigger.click()
                    page.get_by_role("region", name="Recommendation decision review", exact=True).wait_for()
                    if width <= 900:
                        page.get_by_role("button", name="Back to recommendations", exact=True).click()
                    else:
                        page.get_by_role("button", name="Cancel", exact=True).click()
                    assert trigger.evaluate("element => element === document.activeElement"), (width, "missing return focus")
                    nav = page.get_by_role("navigation", name="Recommendation tasks")
                    nav.get_by_role("link", name="Command controls", exact=True).click()
                    page.get_by_role("button", name="Engage kill switch", exact=True).wait_for()
                    page.go_back()
                    page.get_by_role("combobox", name="Recommendation runs", exact=True).wait_for()
                report.append({"revision": revision, "width": width, "height": height, "state": state, **audit})
                page.close()
    browser.close()
(output / "recommendations-layout-audit.json").write_text(json.dumps(report, indent=2) + "\n")
print(f"Recommendation workspace review passed: {len(report)} cases")
