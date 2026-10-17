import os
import sys
from playwright.sync_api import sync_playwright
out, base = sys.argv[1], sys.argv[2]
sections = ['operations', 'assets', 'logic', 'recommendations']
with sync_playwright() as p:
    b = p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH', '/opt/pw-browsers/chromium'))
    pg = b.new_page(viewport={'width': 1440, 'height': 900})
    errs = []
    pg.on('console', lambda m: errs.append(m.text) if m.type == 'error' else None)
    for s in sections:
        pg.goto(f'{base}/#{s}'); pg.wait_for_timeout(900)
        pg.screenshot(path=f'{out}-{s}.png')
    pg.goto(f'{base}/#access'); pg.wait_for_timeout(900); pg.screenshot(path=f'{out}-admin.png')
    pg.get_by_role('tab', name='Roles and permissions').click(); pg.wait_for_timeout(600); pg.screenshot(path=f'{out}-roles.png')
    print('console errors:', errs[:5])
    b.close()
