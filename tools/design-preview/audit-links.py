"""Audit shared destinations against local mock previews; never submit actions."""
import json
import os
import sys
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
scenes = ['overview', 'operations/00000000-0000-4000-8000-000000000101',
          'assets/00000000-0000-4000-8000-000000000011',
          'logic/00000000-0000-4000-8000-000000000500', 'logic/schedules', 'status']
report = []
with sync_playwright() as p:
    options = {'headless': True}
    if os.environ.get('CHROMIUM_PATH'):
        options.update(executable_path=os.environ['CHROMIUM_PATH'], args=['--no-sandbox'])
    browser = p.chromium.launch(**options)
    for revision, base in zip(('before', 'after'), sys.argv[2:4]):
        parsed = urlparse(base)
        assert parsed.scheme == 'http' and parsed.hostname in ('localhost', '127.0.0.1'), 'Local mocks only'
        for width in (1440, 900, 420, 320):
            for scene in scenes:
                page = browser.new_page(viewport={'width': width, 'height': 850}, has_touch=width <= 420,
                                        timezone_id='America/Chicago', reduced_motion='reduce')
                page.context.add_cookies([{'name': 'persona', 'value': 'Admin', 'url': base}])
                writes = []
                page.on('request', lambda r: writes.append(r.url) if r.method not in ('GET', 'HEAD', 'OPTIONS') and '/hubs/' not in r.url else None)
                page.goto(base + '/#' + scene, wait_until='networkidle')
                page.evaluate('document.fonts.ready')
                audit = page.evaluate('''() => {
                    const visible = e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden';
                    const rgb = color => color.match(/[\\d.]+/g).slice(0,3).map(Number);
                    const lum = color => rgb(color).map(v => v/255).map(v => v<=.04045?v/12.92:((v+.055)/1.055)**2.4)
                        .reduce((s,v,i)=>s+v*[.2126,.7152,.0722][i],0);
                    const contrast = (a,b) => (Math.max(lum(a),lum(b))+.05)/(Math.min(lum(a),lum(b))+.05);
                    return {width:innerWidth,scrollWidth:document.documentElement.scrollWidth,
                        links:[...document.querySelectorAll('main a.action-link')].filter(visible).map(e=>{
                            const s=getComputedStyle(e);let bg=e;
                            while(bg.parentElement && getComputedStyle(bg).backgroundColor==='rgba(0, 0, 0, 0)') bg=bg.parentElement;
                            return {label:e.textContent,href:e.getAttribute('href'),appearance:e.dataset.appearance,
                                decoration:s.textDecorationLine,height:e.getBoundingClientRect().height,
                                cue:!!e.querySelector('svg[aria-hidden="true"]'),contrast:contrast(s.color,getComputedStyle(bg).backgroundColor)};
                        })};
                }''')
                if revision == 'after':
                    assert audit['scrollWidth'] <= width, (scene, width, audit)
                    assert any(link['appearance'] == 'record' for link in audit['links']), (scene, 'no record links', audit)
                    for link in audit['links']:
                        assert link['decoration'] == 'none' and link['cue'], (scene, link)
                        assert link['height'] >= (44 if width <= 420 else 24), (scene, link)
                        assert link['contrast'] >= 4.5, (scene, link)
                        assert link['href'].startswith('#'), (scene, link)
                    first = page.locator('main a.action-link[data-appearance="record"]:visible').first
                    page.keyboard.press('Tab')
                    first.focus()
                    focus = first.evaluate('(e) => ({style:getComputedStyle(e).outlineStyle,width:parseFloat(getComputedStyle(e).outlineWidth)})')
                    assert focus['style'] != 'none' and focus['width'] >= 2, (scene, 'focus', focus)
                    # Reuse the actual record's layout with an unbroken identifier and long human name.
                    first.locator('span').first.evaluate("e => e.textContent = 'North Well ' + 'LongAssetIdentifier'.repeat(8)")
                    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), (scene, width, 'long-label overflow')
                assert not writes, writes
                report.append({'revision': revision, 'scene': scene, **audit})
                page.close()
    browser.close()
(output / 'links-audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Link presentation review passed: {len(report)} cases')
