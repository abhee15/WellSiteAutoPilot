"""Check actual long-list/form reachability through wheel and keyboard scrolling.

Usage: python capture-command-scroll.py OUTPUT BEFORE_URL AFTER_URL
Only local mock GETs and in-memory form edits are used; no command is submitted.
"""
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
identity = lambda number: f'{number:08x}-0000-4000-8000-{number:012d}'
commands = [{
    'id': identity(800 + index), 'recommendationId': identity(700),
    'assetId': '00000000-0000-4000-8000-000000000011', 'executionId': identity(101),
    'command': f'SET_CHOKE_{index}', 'value': 40, 'unit': '%', 'status': 'Ambiguous',
    'gateCode': 'SINK_AMBIGUOUS', 'createdAtUtc': datetime.now(timezone.utc).isoformat(), 'canManage': True,
} for index in range(30)]
cases = ((1440, 900, 1), (1000, 600, 1), (900, 820, 1), (420, 850, 1),
         (320, 850, 1), (1440, 500, 1), (1000, 650, 1.25))
report = []

def probe(target):
    return target.evaluate("""element => {
        let top=0,bottom=innerHeight;
        const clips=[];
        for(let parent=element.parentElement;parent;parent=parent.parentElement){
            const style=getComputedStyle(parent),rect=parent.getBoundingClientRect();
            if(['auto','scroll','hidden','clip'].includes(style.overflowY)){
                top=Math.max(top,rect.top+parent.clientTop);
                bottom=Math.min(bottom,rect.top+parent.clientTop+parent.clientHeight);
                clips.push({name:parent.className,overflow:style.overflowY,height:parent.clientHeight,
                    scroll:parent.scrollHeight,scrollTop:parent.scrollTop});
            }
        }
        const rect=element.getBoundingClientRect();
        return {reached:rect.top>=top-2&&rect.bottom<=bottom+2,top:rect.top,bottom:rect.bottom,
            clipTop:top,clipBottom:bottom,clips};
    }""")

def wheel_to_end(page, target):
    # Wheel over the inner list when it owns scrolling, otherwise the surrounding context.
    # A wheel over the header cannot scroll the independently scrollable commands list.
    inner = page.locator('.control-actions-list')
    context = inner if inner.count() and inner.evaluate('e => e.scrollHeight > e.clientHeight + 1') else page.locator('.recommendations-control-context')
    box = context.bounding_box()
    page.mouse.move(box['x'] + min(80, box['width'] / 2), max(100, min(box['y'] + 80, page.viewport_size['height'] - 80)))
    for _ in range(3):
        page.mouse.wheel(0, 100000)
        page.wait_for_timeout(150)
        state = probe(target)
        if state['reached']:
            break
    return state

with sync_playwright() as playwright:
    options = {'headless': True}
    if os.environ.get('CHROMIUM_PATH'):
        options.update(executable_path=os.environ['CHROMIUM_PATH'], args=['--no-sandbox'])
    browser = playwright.chromium.launch(**options)
    for revision, base in zip(('before', 'after'), sys.argv[2:4]):
        parsed = urlparse(base)
        assert parsed.scheme == 'http' and parsed.hostname in ('127.0.0.1', 'localhost'), 'Use local mocks only'
        for width, height, scale in cases:
            for task in ('commands', 'controls'):
                page = browser.new_page(viewport={'width': width, 'height': height}, has_touch=width <= 420,
                                        reduced_motion='reduce', timezone_id='America/Chicago')
                page.context.add_cookies([{'name': 'persona', 'value': 'Admin', 'url': base}])
                def actions_route(route):
                    assert route.request.method == 'GET', 'This audit must never submit a command'
                    route.fulfill(json=commands if task == 'commands' else [])
                page.route(re.compile(r'/api/v1/control/actions(?:[/?]|$)'), actions_route)
                def kill_switch_route(route):
                    assert route.request.method == 'GET', 'This audit must never change command controls'
                    route.fulfill(json={
                        'engaged': True, 'autonomousControlEnabled': True, 'changedBy': 'FIELD\\operator',
                        'changedAtUtc': datetime.now(timezone.utc).isoformat(), 'reason': 'Maintenance verification',
                    })
                page.route('**/api/v1/control/kill-switch', kill_switch_route)
                page.goto(base + '/#recommendations/' + ('actions' if task == 'commands' else 'settings'), wait_until='networkidle')
                if scale != 1:
                    page.add_style_tag(content=f'html {{ font-size: {scale * 100}%; }}')
                page.evaluate('document.fonts.ready')
                if task == 'commands':
                    target = page.get_by_role('button', name='Resolve SET_CHOKE_29', exact=True)
                    target.wait_for(state='attached')
                else:
                    page.get_by_role('button', name='Release kill switch', exact=True).click()
                    target = page.get_by_role('button', name='Release now', exact=True)
                    target.wait_for(state='attached')
                state = wheel_to_end(page, target)
                assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'), (revision, task, width, 'horizontal overflow')
                if revision == 'after':
                    assert state['reached'], (task, width, height, scale, 'wheel cannot reach bottom', state)
                    # Hidden panels must not be the programmatically scrolled workaround.
                    assert page.locator('.control-actions, .kill-switch').evaluate_all('elements=>elements.every(e=>e.scrollTop===0)'), state
                    if width > 900 and height > 576:
                        assert page.locator('.page-body').evaluate('e=>e.scrollTop===0&&e.scrollHeight<=e.clientHeight+1'), state
                    if task == 'commands':
                        first = page.get_by_role('button', name='Verify SET_CHOKE_0', exact=True)
                        first.focus()
                        page.keyboard.press('PageDown')
                        page.keyboard.press('End')
                        page.wait_for_timeout(200)
                        assert probe(target)['reached'], (width, height, 'keyboard End cannot reach last row')
                        first.focus()
                        for _ in range(len(commands) * 4 + 8):
                            if target.evaluate('e=>e===document.activeElement'):
                                break
                            page.keyboard.press('Tab')
                        assert target.evaluate('e=>e===document.activeElement'), (width, 'last action absent from tab order')
                        assert probe(target)['reached'], (width, 'focused last action is clipped')
                        target.click()
                        target = page.get_by_role('button', name='Record decision', exact=True)
                        target.wait_for(state='attached')
                        expanded = wheel_to_end(page, target)
                        assert expanded['reached'], (width, height, 'resolution footer clipped', expanded)
                    else:
                        page.get_by_role('checkbox', name='I understand that approved commands can be sent again.', exact=True).check()
                        assert target.is_enabled()
                        assert wheel_to_end(page, target)['reached'], (width, height, 'release footer clipped')
                page.screenshot(path=str(output / f'{revision}-{width}-{height}-{scale}-{task}.png'))
                report.append({'revision': revision, 'width': width, 'height': height, 'textScale': scale, 'task': task, **state})
                page.close()
    browser.close()
(output / 'command-scroll-audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Command scrolling review passed: {len(report)} cases')
