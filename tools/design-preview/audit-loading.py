"""Exercise delayed panel reads and terminal states on local mocks; never mutate data."""
import json
import os
import re
import sys
import time
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
scenes = {
    'connections': (r'/api/v1/connections$', '.connections-layout > .panel:first-child'),
    'access': (r'/api/v1/security/users/page\?', '.users-directory-layout .split-browse'),
    'status': (r'/api/v1/(?:connections$|system/info$)', '.status-page > section'),
}
report = []
with sync_playwright() as p:
    options = {'headless': True}
    if os.environ.get('CHROMIUM_PATH'):
        options.update(executable_path=os.environ['CHROMIUM_PATH'], args=['--no-sandbox'])
    browser = p.chromium.launch(**options)
    for revision, base in zip(('before', 'after'), sys.argv[2:4]):
        url = urlparse(base)
        assert url.scheme == 'http' and url.hostname in ('127.0.0.1', 'localhost'), 'Local mocks only'
        for width in (1440, 900, 420, 320):
            for scene, (pattern, panel_selector) in scenes.items():
                for terminal in ('ready', 'empty', 'failed'):
                    page = browser.new_page(viewport={'width': width, 'height': 850}, reduced_motion='reduce')
                    page.context.add_cookies([{'name': 'persona', 'value': 'Admin', 'url': base}])
                    print(f'{revision} {width}px {scene} {terminal}', flush=True)
                    held, writes = [], []
                    if scene == 'status':
                        # Deliberately start the service read later than the connection read. Placeholders alone
                        # do not prove that every request has reached the response gate.
                        page.add_init_script("""(() => {
                            const fetch = window.fetch.bind(window);
                            window.fetch = (input, init) => String(typeof input === 'string' ? input : input.url).includes('/api/v1/system/info')
                                ? new Promise(resolve => setTimeout(resolve, 200)).then(() => fetch(input, init)) : fetch(input, init);
                        })()""")
                    def hold(route):
                        # Read the disposable mock now, but withhold its response from the app.
                        response = route.fetch()
                        held.append((route, response.json()))
                    page.route(re.compile(pattern), hold)
                    page.on('request', lambda r: writes.append(r.url) if r.method not in ('GET', 'HEAD', 'OPTIONS') and '/hubs/' not in r.url else None)
                    page.goto(base + '/#' + scene, wait_until='domcontentloaded')
                    panels = page.locator(panel_selector)
                    panels.first.locator('.loading-indicator').wait_for()
                    if scene == 'status':
                        panels.nth(1).locator('.loading-indicator').wait_for()
                    expected = {'/api/v1/connections', '/api/v1/system/info'} if scene == 'status' else {
                        '/api/v1/connections' if scene == 'connections' else '/api/v1/security/users/page'}
                    def await_reads():
                        deadline = time.monotonic() + 5
                        captured = lambda: {urlparse(route.request.url).path for route, _ in held}
                        while not expected.issubset(captured()) and time.monotonic() < deadline:
                            page.wait_for_timeout(20)
                        assert expected.issubset(captured()), (scene, 'required reads not captured', captured())
                    await_reads()
                    initial = panels.evaluate_all('''panels => panels.map(panel=>{
                        const loader=panel.querySelector('.loading-indicator'),state=panel.querySelector('.panel-loading-state');
                        const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,cx:r.x+r.width/2,cy:r.y+r.height/2}};
                        return {panel:rect(panel),loader:rect(loader),state:state?rect(state):null,
                            statuses:panel.querySelectorAll('[role="status"]').length};
                    })''')
                    if revision == 'after':
                        for item in initial:
                            assert item['state'] and item['statuses'] == 1, (scene, width, item)
                            assert abs(item['loader']['cx']-item['state']['cx']) <= 2, (scene, width, 'horizontal center', item)
                            assert abs(item['loader']['cy']-item['state']['cy']) <= 2, (scene, width, 'vertical center', item)
                            assert item['state']['height'] >= 160, (scene, width, 'collapsed loading region', item)
                        nav = page.get_by_role('link', name='Overview', exact=True)
                        assert nav.is_enabled() and nav.evaluate('e=>!e.closest("[inert]")')
                    def release():
                        pending = list(held)
                        held.clear()
                        for route, body in pending:
                            if terminal == 'failed':
                                route.fulfill(status=503, json={})
                            elif terminal == 'empty':
                                route.fulfill(json={'components': []} if '/system/info' in route.request.url else
                                              {'items': [], 'nextCursor': None} if '/users/page' in route.request.url else [])
                            else:
                                route.fulfill(json=body)
                    release()
                    panels.first.locator('.loading-indicator').wait_for(state='detached')
                    if scene == 'status':
                        panels.nth(1).locator('.loading-indicator').wait_for(state='detached')
                    assert page.locator('.panel-loading-state').count() == 0
                    row_selector = {'connections': '.connection-row', 'access': 'tbody tr', 'status': '.health-row'}[scene]
                    if terminal == 'ready':
                        assert panels.locator(row_selector).count() > 0, (scene, 'ready content missing')
                    elif terminal == 'empty':
                        assert panels.locator(row_selector).count() == 0, (scene, 'empty read retained rows')
                    if terminal == 'failed':
                        assert panels.locator('[role="alert"]').count() > 0, (scene, 'failure hidden')
                    if revision == 'after' and scene == 'status' and terminal == 'ready':
                        rows = page.locator('.health-row').count()
                        page.locator('.status-actions button').click()
                        page.wait_for_function('document.querySelectorAll(".health-row").length > 0')
                        assert page.locator('.health-row').count() == rows
                        assert page.locator('.panel-loading-state').count() == 0, 'background refresh must retain content'
                        # Wait until both independently fetched responses are held.
                        await_reads()
                        release()
                    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), (revision, scene, width, terminal, 'overflow')
                    assert not writes, writes
                    report.append({'revision': revision, 'scene': scene, 'width': width, 'terminal': terminal, 'initial': initial})
                    page.close()
    browser.close()
(output / 'loading-audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Panel loading review passed: {len(report)} cases')
