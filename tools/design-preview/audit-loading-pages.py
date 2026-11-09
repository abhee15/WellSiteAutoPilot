"""Hold every page's data reads and measure where its loading indicators render.

Runs against the local mock preview only and never mutates data. For each route it answers every request except the
signed-in user's session, so each page stays in its loading state, then checks the contract in
docs/design/loading-states.md: an indicator inside a panel is inset by the panel gutter and a first-content loader is
centered in its region. Output is a JSON record per route and width. Only the 'after' build is held to the rules; the
'before' build is recorded so a reviewer can see what changed.
"""
import json
import os
import re
import sys
from pathlib import Path
from urllib.parse import urlparse

from playwright.sync_api import sync_playwright

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)

ID_ASSET = '00000000-0000-4000-8000-000000000011'
ID_LOGIC = '00000000-0000-4000-8000-000000000500'
ROUTES = {
    'overview': '#overview',
    'operations': '#operations',
    'recommendations': '#recommendations',
    'assets': '#assets',
    'assets-detail': f'#assets/{ID_ASSET}',
    'assets-types': '#assets/types',
    'logic': '#logic',
    'logic-detail': f'#logic/{ID_LOGIC}',
    'logic-modules': '#logic/modules',
    'connections': '#connections',
    'access-users': '#access',
    'access-roles': '#access/roles',
    'access-grants': '#access/grants',
    'access-activity': '#access/activity',
    'status': '#status',
}
GUTTER_MIN = 12  # the panel gutter is 18px; anything below 12px is flush against the edge.

MEASURE = """() => {
  const rect = e => { const r = e.getBoundingClientRect(); return {x: r.x, y: r.y, width: r.width, height: r.height, cx: r.x + r.width / 2, cy: r.y + r.height / 2}; };
  // Only indicators a person can see: a pane that the responsive layout hides (display: none) has no boxes.
  const shown = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
  return Array.from(document.querySelectorAll('.loading-indicator, .loading-skeleton')).filter(shown).map(el => {
    const panel = el.closest('.panel');
    const state = el.closest('.panel-loading-state');
    const spinner = el.querySelector('.loading-spinner') ?? el;
    const label = (el.textContent || '').trim().slice(0, 60);
    return {
      label, size: el.getAttribute('data-size') || 'medium', inline: el.hasAttribute('data-inline'),
      pane: state ? 'panel-loading' : 'other', contentLeft: rect(spinner).x,
      indicator: rect(el), panel: panel ? rect(panel) : null, state: state ? rect(state) : null,
    };
  });
}"""

report, failures = [], []
with sync_playwright() as p:
    options = {'headless': True}
    if os.environ.get('CHROMIUM_PATH'):
        options.update(executable_path=os.environ['CHROMIUM_PATH'], args=['--no-sandbox'])
    browser = p.chromium.launch(**options)
    for revision, base in zip(('before', 'after'), sys.argv[2:4]):
        url = urlparse(base)
        assert url.scheme == 'http' and url.hostname in ('127.0.0.1', 'localhost'), 'Local mocks only'
        for width in (1440, 420):
            for name, fragment in ROUTES.items():
                page = browser.new_page(viewport={'width': width, 'height': 850}, reduced_motion='reduce')
                page.context.add_cookies([{'name': 'persona', 'value': 'Admin', 'url': base}])
                writes = []
                page.on('request', lambda r: writes.append(r.url) if r.method not in ('GET', 'HEAD', 'OPTIONS') and '/hubs/' not in r.url else None)

                def hold(route):
                    # Answer the session so the shell renders; leave every other read unanswered.
                    if route.request.method != 'GET' or re.search(r'/api/v1/security/me(?:\?|$)', route.request.url):
                        route.continue_()
                    # Otherwise do nothing: the request stays pending and the page stays in its loading state.
                page.route(re.compile(r'/api/v1/'), hold)
                print(f'{revision} {width}px {name}', flush=True)
                page.goto(f'{base}/{fragment}', wait_until='domcontentloaded')
                page.locator('.page-body').first.wait_for(timeout=20000)
                # Indicators appear after a short delay so a fast read never flashes one.
                page.wait_for_timeout(900)
                found = page.evaluate(MEASURE)
                record = {'revision': revision, 'route': name, 'width': width, 'indicators': found}
                report.append(record)
                if revision == 'after':
                    for item in found:
                        where = f'{name} @{width}px "{item["label"]}"'
                        if item['inline']:
                            continue
                        if item['panel'] is not None and item['pane'] == 'other':
                            inset = item['contentLeft'] - item['panel']['x']
                            if inset < GUTTER_MIN:
                                failures.append(f'{where}: flush against the panel edge ({inset:.0f}px, needs {GUTTER_MIN}px)')
                        if item['pane'] == 'panel-loading':
                            if abs(item['indicator']['cx'] - item['state']['cx']) > 2:
                                failures.append(f'{where}: not centered in its loading region')
                            if item['state']['height'] < 160:
                                failures.append(f'{where}: loading region collapsed to {item["state"]["height"]:.0f}px')
                    if not page.evaluate('document.documentElement.scrollWidth <= innerWidth'):
                        failures.append(f'{name} @{width}px: horizontal overflow while loading')
                    if writes:
                        failures.append(f'{name} @{width}px: unexpected write requests {writes}')
                # Cancel the unanswered handlers first so closing the page does not leave pending tasks.
                try:
                    page.unroute_all(behavior='ignoreErrors')
                except Exception:
                    pass
                page.close()
    browser.close()
(output / 'loading-pages.json').write_text(json.dumps(report, indent=2) + '\n')
seen = sum(len(r['indicators']) for r in report if r['revision'] == 'after')
print(f'Loading indicators measured across {len(ROUTES)} routes: {seen} found in the after build')
if failures:
    print('\n'.join(sorted(set(failures))))
    sys.exit(1)
