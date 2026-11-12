"""Capture before/after real-browser evidence for the shared tab system.

The preview is backed by tools/design-preview/mock-server.mjs. No provider, server,
database or authentication secret is used. Output is visual evidence, not a
pixel-perfect screenshot gate: reviewers inspect the changes deliberately.
"""
import json
import os
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

OUT = Path(sys.argv[1])
BEFORE = sys.argv[2].rstrip("/")
AFTER = sys.argv[3].rstrip("/")
OUT.mkdir(parents=True, exist_ok=True)

STATES = {
    "logic-list": "#logic",
    "logic-detail": "#logic/00000000-0000-4000-8000-000000000500",
    "logic-modules": "#logic/modules",
    "logic-schedules": "#logic/schedules",
    "assets-list": "#assets",
    "assets-detail": "#assets/00000000-0000-4000-8000-000000000011",
    "assets-types": "#assets/types",
}
WIDTHS = [(1440, 900), (900, 820), (420, 850)]
report = []
failures = []

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, executable_path=os.environ.get("WSA_CHROMIUM_PATH"))
    for revision, base in [("before", BEFORE), ("after", AFTER)]:
        for width, height in WIDTHS:
            context = browser.new_context(viewport={"width": width, "height": height},
                                          device_scale_factor=1, reduced_motion="reduce")
            for state, fragment in STATES.items():
                # A fresh page makes hash routes full navigations with HTTP responses.
                page = context.new_page()
                target = f"{base}/{fragment}"
                try:
                    response = page.goto(target, wait_until="networkidle", timeout=30000)
                    if not response or response.status >= 400:
                        raise AssertionError(f"HTTP {response.status if response else 'none'} for {target}")
                    page.locator(".split-tabs, .tab-bar").first.wait_for(timeout=15000)
                    if revision == "after" and state == "logic-list" and os.environ.get("LOGIC_CONFIGURATIONS_PREVIEW") == "1":
                        group = page.get_by_role("button", name="ESP frequency optimizer", exact=True)
                        group.wait_for()
                        page.get_by_text("ForeSite System B (foresite)", exact=True).wait_for()
                        group.focus()
                        page.keyboard.press("Enter")
                        if group.get_attribute("aria-expanded") != "false":
                            raise AssertionError("Keyboard did not collapse the module")
                        page.keyboard.press("Space")
                        deployment = page.get_by_role("button", name="Inspect revisions of ESP frequency — East field")
                        deployment.wait_for()
                        if page.locator(".configuration-list").evaluate("(table) => table.scrollWidth > table.parentElement.clientWidth"):
                            raise AssertionError("Configurations require horizontal scrolling")
                        deployment.click()
                        page.get_by_role("tab", name="Overview", exact=True).wait_for()
                        page.go_back()
                        deployment.wait_for()
                        page.mouse.move(0, 0)
                    page.evaluate("document.fonts.ready")
                    page.wait_for_timeout(400)
                    filename = OUT / f"{revision}-{width}-{state}.png"
                    page.screenshot(path=str(filename), full_page=False, animations="disabled")
                    audit = page.evaluate("""() => {
                      const bar = document.querySelector('.tab-bar, .split-tabs');
                      const strip = document.querySelector('.tab-strip, .split-tabs');
                      const selected = strip?.querySelector('[aria-current="page"], [aria-selected="true"]');
                      const items = Array.from(strip?.querySelectorAll('a, [role="tab"]') || []);
                      const bounds = el => {
                        const rect = el?.getBoundingClientRect();
                        return rect ? {left: Math.round(rect.left), right: Math.round(rect.right),
                          top: Math.round(rect.top), bottom: Math.round(rect.bottom),
                          width: Math.round(rect.width), height: Math.round(rect.height)} : null;
                      };
                      const style = selected ? getComputedStyle(selected) : null;
                      return {
                        outerDocumentWidth: document.documentElement.scrollWidth,
                        viewportWidth: innerWidth,
                        tabBar: bounds(bar), strip: bounds(strip),
                        scrollWidth: strip?.scrollWidth ?? 0, clientWidth: strip?.clientWidth ?? 0,
                        scrollable: (strip?.scrollWidth ?? 0) > (strip?.clientWidth ?? 0),
                        selectedText: selected?.textContent?.trim() ?? null,
                        selectedRole: selected?.getAttribute('role') ?? 'link',
                        selectedIndicator: style?.borderBottomColor ?? null,
                        selectedFontSize: style?.fontSize ?? null,
                        items: items.map(el => ({text: el.textContent?.trim(), bounds: bounds(el)})),
                        action: bounds(document.querySelector('.tab-bar-action')),
                        focusVisibleStyle: getComputedStyle(items[0] || strip).outlineStyle,
                      };
                    }""")
                    report.append({"revision": revision, "viewport": f"{width}x{height}",
                                   "state": state, "screenshot": str(filename.name), **audit})
                except Exception as exc:
                    failures.append(f"{revision} {width} {state}: {type(exc).__name__}: {exc}")
                finally:
                    page.close()
            context.close()
    browser.close()

(OUT / "tab-layout-audit.json").write_text(json.dumps(report, indent=2))
(OUT / "capture-errors.txt").write_text("\n".join(failures))
print(f"Captured {len(report)} screenshots; errors: {len(failures)}")
for failure in failures:
    print(f"ERROR {failure}")
if failures:
    sys.exit(1)
