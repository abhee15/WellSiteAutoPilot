"""Build-independent browser checks and screenshots for the shared native field owners.

Usage: python capture-shared-forms.py FIXTURE_DIST OUTPUT_DIRECTORY
Playwright is installed by the existing visual-review workflow. CHROMIUM_PATH is optional.
The served content is a static fixture: no provider, API, database or auth proxy is used.
"""
import functools
import re
from datetime import datetime, timedelta, timezone
from urllib.parse import urlparse
import json
import os
import sys
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from playwright.sync_api import sync_playwright


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_args):
        pass


root, output = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
server = ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(QuietHandler, directory=str(root)))
threading.Thread(target=server.serve_forever, daemon=True).start()
report = []
try:
    with sync_playwright() as playwright:
        options = {"headless": True}
        if os.environ.get("CHROMIUM_PATH"):
            options.update(executable_path=os.environ["CHROMIUM_PATH"], args=["--no-sandbox"])
        browser = playwright.chromium.launch(**options)
        for width in (1440, 900, 420, 320):
            page = browser.new_page(viewport={"width": width, "height": 900}, has_touch=width <= 420)
            page.goto(f"http://127.0.0.1:{server.server_port}/tests/visual/shared-forms.html")
            page.get_by_role("heading", name="Shared form review").wait_for()
            page.evaluate("document.fonts.ready")
            state = page.evaluate("""() => {
                const probe = document.createElement('span');
                probe.style.cssText = 'position:absolute;visibility:hidden;font-size:var(--font-size-control);font-weight:var(--font-weight-medium);height:var(--size-control)';
                document.body.append(probe);
                const expected = {font:getComputedStyle(probe).fontSize, weight:getComputedStyle(probe).fontWeight, regular:getComputedStyle(document.documentElement).getPropertyValue('--font-weight-regular').trim()};
                probe.style.fontSize = 'var(--font-size-label)';
                expected.labelFont = getComputedStyle(probe).fontSize;
                const coarse = matchMedia('(pointer: coarse)').matches;
                if (coarse) probe.style.height = 'var(--size-touch-target)';
                expected.height = parseFloat(getComputedStyle(probe).height);
                probe.style.outline = 'var(--focus-ring-width) solid var(--color-focus-ring)';
                expected.focus = getComputedStyle(probe).outlineWidth;
                probe.remove();
                const luminance = color => {
                    const c = color.match(/[\\d.]+/g).slice(0, 3).map(Number).map(v => {
                        v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4;
                    });
                    return c[0] * .2126 + c[1] * .7152 + c[2] * .0722;
                };
                const contrast = (a, b) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05);
                return {width:innerWidth, documentWidth:document.documentElement.scrollWidth, coarse, expected,
                    controls:[...document.querySelectorAll('.ui-field-control:not(:disabled)')].map(e => {
                        const c = getComputedStyle(e); return {height:e.getBoundingClientRect().height, font:c.fontSize, weight:c.fontWeight,
                            borderContrast:contrast(c.borderTopColor, c.backgroundColor), textContrast:contrast(c.color, c.backgroundColor)};
                    }), labels:[...document.querySelectorAll('.ui-field-label, .ui-radio-group legend')].map(e => ({font:getComputedStyle(e).fontSize, weight:getComputedStyle(e).fontWeight})),
                    targets:[...document.querySelectorAll('.ui-choice')].map(e => e.getBoundingClientRect().height)};
            }""")
            assert state["documentWidth"] <= width, f"{width}: document overflow"
            assert state["controls"], f"{width}: fixture has no controls"
            for control in state["controls"]:
                assert control["height"] >= state["expected"]["height"], (width, control)
                assert control["font"] == state["expected"]["font"], (width, control)
                assert control["weight"] == state["expected"]["regular"], (width, control)
                assert control["borderContrast"] >= 3, (width, control)
                assert control["textContrast"] >= 4.5, (width, control)
            for label in state["labels"]:
                assert label == {"font": state["expected"]["labelFont"], "weight": state["expected"]["weight"]}, (width, label)
            for height in state["targets"]:
                assert height >= state["expected"]["height"], (width, height)
            date = page.get_by_label("Start date", exact=False)
            date.focus()
            assert date.evaluate("e => getComputedStyle(e).outlineWidth") == state["expected"]["focus"]
            page.screenshot(path=str(output / f"fields-{width}.png"))
            page.locator('.ui-confirm-panel').scroll_into_view_if_needed()
            page.screenshot(path=str(output / f"confirmation-notices-{width}.png"))
            report.append(state)
            page.close()
        # Optional whole-page evidence uses only the job's disposable local mock servers.
        workflow_report = []
        for revision, base in zip(("before", "after"), sys.argv[3:5]):
            parsed = urlparse(base)
            assert parsed.scheme == "http" and parsed.hostname in ("127.0.0.1", "localhost"), "Use local mock previews only"
            for width in (1440, 900, 420, 320):
                page = browser.new_page(viewport={"width": width, "height": 900}, has_touch=width <= 420, timezone_id="America/Chicago")
                page.context.add_cookies([{"name": "persona", "value": "Admin", "url": base}])
                now = datetime.now(timezone.utc)
                identity = lambda n: f"00000000-0000-4000-8000-{n:012d}"
                records = [{"id": identity(700), "assetId": identity(11), "executionId": identity(101),
                    "configuredLogicId": identity(500), "configurationRevisionId": identity(300),
                    "moduleId": "esp-frequency-optimizer", "moduleVersion": "1.2.0", "status": "Created", "canDecide": True,
                    "command": "Set pump speed", "quantity": "PumpSpeed", "suggestedValue": 48, "unit": "Hz",
                    "createdAtUtc": now.isoformat(), "expiresAtUtc": (now + timedelta(hours=1)).isoformat()}]
                records.append({**records[0], "id": identity(702), "status": "Approved", "decisionBy": "FIELD\\reviewer", "decisionAtUtc": now.isoformat()})
                pending = []
                def recommendation_route(route):
                    request = route.request
                    if request.method == "POST":
                        pending.append(route)
                    elif request.url.split("?")[0].endswith(tuple(record["id"] for record in records)):
                        route.fulfill(json=next(record for record in records if request.url.split("?")[0].endswith(record["id"])))
                    else:
                        route.fulfill(json=records)
                page.route(re.compile(r"/api/v1/recommendations(?:[/?]|$)"), recommendation_route)
                page.route("**/api/v1/control/recommendations/*/execute", lambda route: route.fulfill(json={"executed": False, "refusalCode": "GATE_KILL_SWITCH", "action": None}))
                page.goto(base + "/#recommendations/" + identity(700))
                review = page.get_by_role("region", name="Recommendation decision review", exact=True)
                review.wait_for()
                page.evaluate("document.fonts.ready")
                page.screenshot(path=str(output / f"{revision}-{width}-decision.png"))
                approve = review.get_by_role("radio", name="Approve recommendation")
                approve.focus()
                page.keyboard.press("ArrowRight")
                assert review.get_by_role("radio", name="Reject recommendation").is_checked()
                approve.check()
                reason = review.get_by_role("textbox", name=re.compile("Reason"))
                if revision == "after":
                    field_style = reason.evaluate("""element => {
                        const probe = document.createElement('span');
                        probe.style.fontSize = 'var(--font-size-control)';
                        document.body.append(probe);
                        const expected = getComputedStyle(probe).fontSize;
                        probe.remove();
                        return {font:getComputedStyle(element).fontSize, expected};
                    }""")
                    assert field_style["font"] == field_style["expected"], (width, field_style)
                reason.fill("x")
                reason.blur()
                if revision == "after":
                    assert reason.get_attribute("aria-invalid") == "true"
                    page.screenshot(path=str(output / f"{revision}-{width}-invalid.png"))
                reason.fill("Reviewed operating limits")
                acknowledgement = review.get_by_role("checkbox")
                acknowledgement.check()
                submit = review.get_by_role("button", name="Submit decision", exact=True)
                submit.click()
                review.get_by_role("status").wait_for()
                assert reason.is_disabled() and acknowledgement.is_disabled() and approve.is_disabled() and submit.is_disabled()
                assert review.get_by_role("status").count() == 1
                assert page.get_by_role("dialog").count() == 0
                submit.evaluate("element => element.click()")
                assert len(pending) == 1
                page.screenshot(path=str(output / f"{revision}-{width}-busy.png"))
                pending[0].fulfill(status=503, json={})
                review.get_by_role("alert").wait_for()
                assert reason.input_value() == "Reviewed operating limits"
                page.screenshot(path=str(output / f"{revision}-{width}-error.png"))
                submit.click()
                review.get_by_role("status").wait_for()
                assert len(pending) == 2
                records[0].update(status="Approved", decisionBy="FIELD\\reviewer", decisionAtUtc=now.isoformat())
                pending[1].fulfill(json={"id": identity(700), "status": "Approved", "decisionBy": "FIELD\\reviewer", "decisionAtUtc": now.isoformat()})
                review.wait_for(state="detached")
                page.get_by_role("status").filter(has_text=re.compile("decision", re.I)).wait_for()
                page.screenshot(path=str(output / f"{revision}-{width}-saved.png"))
                page.goto(base + "/#recommendations/" + identity(702))
                execute = page.get_by_role("region", name="Execute approved recommendation", exact=True)
                execute.wait_for()
                assert execute.get_by_role("textbox").count() == 0
                execute.get_by_role("checkbox").check()
                execute.get_by_role("button", name="Execute command", exact=True).click()
                execute.get_by_role("alert").wait_for()
                assert not execute.get_by_role("checkbox").is_checked()
                assert execute.get_by_role("button", name="Execute command", exact=True).is_disabled()
                assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
                page.screenshot(path=str(output / f"{revision}-{width}-refusal.png"))
                workflow_report.append({"revision": revision, "width": width, "keyboard": "passed", "single_submission": "passed", "preserved_reason": "passed", "saved_outcome": "passed", "refusal": "passed"})
                page.close()
        (output / "recommendation-workflow-audit.json").write_text(json.dumps(workflow_report, indent=2) + "\n")
        browser.close()
finally:
    server.shutdown()
    server.server_close()
    (output / "field-layout-audit.json").write_text(json.dumps(report, indent=2) + "\n")
print(f"Shared native form layout passed at {len(report)} widths")
