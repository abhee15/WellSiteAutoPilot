"""Compare shared typography roles and Overview states using disposable local mock previews.

Usage: python capture-typography.py OUTPUT BEFORE_URL AFTER_URL
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
sections = ('overview', 'operations', 'recommendations', 'assets', 'logic', 'connections', 'status', 'access')
widths = ((1440, 900), (1024, 850), (420, 850), (320, 850))
report = []

with sync_playwright() as playwright:
    options = {'headless': True}
    if os.environ.get('CHROMIUM_PATH'):
        options.update(executable_path=os.environ['CHROMIUM_PATH'], args=['--no-sandbox'])
    browser = playwright.chromium.launch(**options)
    for revision, base in zip(('before', 'after'), sys.argv[2:4]):
        parsed = urlparse(base)
        assert parsed.scheme == 'http' and parsed.hostname in ('127.0.0.1', 'localhost'), 'Use local mocks only'
        for width, height in widths:
            cases = [(section, 'populated') for section in sections] + [
                ('overview', state) for state in ('clear', 'unavailable', 'restricted')]
            for section, state in cases:
                page = browser.new_page(viewport={'width': width, 'height': height}, has_touch=width <= 420,
                                        timezone_id='America/Chicago', reduced_motion='reduce')
                page.context.add_cookies([{'name': 'persona', 'value': 'Admin', 'url': base}])
                if state in ('clear', 'unavailable'):
                    code = 503 if state == 'unavailable' else 200
                    page.route(re.compile(r'/api/v1/recommendations(?:[/?]|$)'), lambda route: route.fulfill(status=code, json=[]))
                    page.route(re.compile(r'/api/v1/control/actions\?'), lambda route: route.fulfill(status=code, json=[]))
                    if state == 'clear':
                        page.route(re.compile(r'/api/v1/executions(?:[/?]|$)'), lambda route: route.fulfill(json=[]))
                if state == 'restricted':
                    def restricted(route):
                        user = route.fetch().json()
                        user['permissions'] = ['executions.read']
                        route.fulfill(json=user)
                    page.route('**/api/v1/security/me', restricted)
                page.goto(base + '/#' + section, wait_until='networkidle')
                page.get_by_role('heading', level=1).wait_for()
                # Pages only request weights they use. Load the registered faces before checking availability.
                faces = page.evaluate("Promise.all(['400 14px Roboto','500 18px Roboto','700 28px Roboto'].map(font => document.fonts.load(font).then(faces => faces.length)))")
                page.evaluate('document.fonts.ready')
                audit = page.evaluate("""() => {
                    const visible = e => e.getClientRects().length;
                    const style = e => ({size:getComputedStyle(e).fontSize,weight:getComputedStyle(e).fontWeight});
                    const probe = document.createElement('span');
                    document.body.append(probe);
                    const token = name => {probe.style.fontSize=`var(${name})`;return getComputedStyle(probe).fontSize};
                    const expected = {section:token('--font-size-section-heading'),metric:token('--font-size-metric'),
                        body:token('--font-size-body'),support:token('--font-size-body-small')};
                    probe.remove();
                    const grid=document.querySelector('.overview-top');
                    return {viewport:innerWidth,documentWidth:document.documentElement.scrollWidth,expected,
                        sectionHeadings:[...document.querySelectorAll('.panel-header h2')].filter(visible).map(style),
                        metrics:[...document.querySelectorAll('.summary-card[data-density="default"] > strong')].filter(visible).map(style),
                        compact:[...document.querySelectorAll('.summary-card[data-density="compact"] > strong')].filter(visible).map(style),
                        support:[...document.querySelectorAll('.operator-item-context > small')].filter(visible).map(style),
                        topColumns:grid?getComputedStyle(grid).gridTemplateColumns:null,
                        fontLoaded:document.fonts.check('400 14px Roboto')&&document.fonts.check('500 18px Roboto')&&document.fonts.check('700 28px Roboto')};
                }""")
                assert audit['documentWidth'] <= width, (revision, section, state, width, 'horizontal overflow', audit)
                if revision == 'after':
                    assert all(faces) and audit['fontLoaded'], (section, 'missing font face', faces)
                    for item in audit['sectionHeadings']:
                        assert item == {'size': audit['expected']['section'], 'weight': '500'}, (section, item, audit)
                    for item in audit['metrics']:
                        assert item == {'size': audit['expected']['metric'], 'weight': '700'}, (section, item, audit)
                    for item in audit['compact']:
                        assert item == {'size': audit['expected']['body'], 'weight': '500'}, (section, item, audit)
                    for item in audit['support']:
                        assert item['size'] == audit['expected']['support'], (section, item, audit)
                    if section == 'overview':
                        if width == 1440:
                            assert len(audit['topColumns'].split()) == 2, ('wide layout', audit)
                        else:
                            assert len(audit['topColumns'].split()) == 1, ('stacked layout', audit)
                        if state == 'clear':
                            assert page.get_by_text('Nothing needs your attention right now.', exact=True).is_visible()
                        if state == 'unavailable':
                            assert page.get_by_text('Some attention items could not be loaded.', exact=True).is_visible()
                            assert page.get_by_text('Nothing needs your attention right now.', exact=True).count() == 0
                        if state == 'restricted':
                            metrics = page.get_by_role('region', name='At a glance', exact=True)
                            assert metrics.get_by_role('link').count() == 1
                page.screenshot(path=str(output / f'{revision}-{width}-{section}-{state}.png'))
                report.append({'revision': revision, 'section': section, 'state': state, 'width': width, **audit})
                page.close()
    browser.close()
(output / 'typography-audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Typography and Overview review passed: {len(report)} cases')
