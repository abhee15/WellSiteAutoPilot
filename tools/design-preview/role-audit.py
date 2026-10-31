# Walks each page and tab as the named roles against the mock server and prints what each role is offered.
# A local aid, like the rest of this folder: read the output for anything a role should (not) see.
import json, sys, re
from playwright.sync_api import sync_playwright
# Usage: python3 role-audit.py http://127.0.0.1:5199 /tmp/role-audit Operator Engineer Admin
BASE=sys.argv.pop(1)
OUT=sys.argv.pop(1)
import os; os.makedirs(OUT, exist_ok=True)
JS="""()=>{
 const vis=e=>{const r=e.getBoundingClientRect();const s=getComputedStyle(e);return r.width>0&&r.height>0&&s.visibility!=='hidden'};
 const name=e=>(e.getAttribute('aria-label')||e.textContent||'').replace(/\\s+/g,' ').trim().slice(0,60);
 return {h:[...document.querySelectorAll('h1,h2')].filter(vis).map(name),
  buttons:[...document.querySelectorAll('main button, .page-body button')].filter(vis).map(e=>name(e)+(e.disabled?' (disabled)':'')).filter(Boolean),
  alerts:[...document.querySelectorAll('[role=alert],.status-error')].filter(vis).map(name)}}"""
# step: (label, hash, [actions]) ; action = ('click', role, name_regex) | ('row',) first table row | ('wait',ms)
SCEN=[
 ('Runs: open a run','#operations',[('click','button',r'Inspect execution')]),
 ('Assets: open a well','#assets',[('click','button',r'Open Well 7-12H')]),
 ('Assets: open a pad','#assets',[('click','button',r'Open Permian Pad 7')]),
 ('Asset types: details of Well','#assets',[('click','tab',r'Asset types'),('click','button',r'Show details of Well')]),
 ('Asset types: details of Pad','#assets',[('click','tab',r'Asset types'),('click','button',r'Show details of Pad')]),
 ('Logic: open configured logic','#logic',[('click','button',r'Inspect revisions')]),
 ('Logic: Configure logic form','#logic',[('click','button',r'Configure logic')]),
 ('Modules: about 1.2.0 (trusted)','#logic',[('click','tab',r'Modules'),('click','button',r'About ESP')]),
 ('Connections: open one','#connections',[('click','button',r'ForeSite')]),
 ('Access: assign a role','#access',[('click','button',r'Assign a role to Morgan')]),
 ('Access: Roles edit','#access',[('click','tab',r'Roles and permissions'),('click','button',r'^Edit$')]),
]
out={}
with sync_playwright() as p:
    b=p.chromium.launch(executable_path='/opt/pw-browsers/chromium')
    for persona in sys.argv[1:]:
        ctx=b.new_context(viewport={'width':1440,'height':900}); ctx.add_cookies([{'name':'persona','value':persona,'url':BASE}])
        pg=ctx.new_page(); errs=[]
        pg.on('pageerror',lambda e:errs.append(str(e)))
        pg.on('console',lambda m:errs.append(m.text) if m.type=='error' and '404' not in m.text else None)
        res={}
        for label,hsh,acts in SCEN:
            pg.goto(BASE+'/'); pg.goto(BASE+'/'+hsh); pg.wait_for_timeout(900)
            note=''
            for a in acts:
                loc=pg.get_by_role(a[1],name=re.compile(a[2])).first
                try:
                    loc.click(timeout=1500); pg.wait_for_timeout(500)
                except Exception as e:
                    note='(not available)'; break
            r=pg.evaluate(JS); r['note']=note
            res[label]=r
            pg.screenshot(path=f"{OUT}/{persona}-{re.sub('[^a-z]+','-',label.lower())}.png")
        res['_errors']=errs; out[persona]=res; ctx.close()
    b.close()
json.dump(out,open(OUT+'/role-audit.json','w'),indent=1)
labels=[l for l,_,_ in SCEN]
for label in labels:
    print('###',label)
    for persona in out:
        r=out[persona][label]
        btn=[x for x in r['buttons'] if not x.startswith(('Open ','Inspect execution','Hide children'))]
        print(f"  [{persona}] {r['note']} h={r['h'][:4]} alerts={r['alerts']}\n      btn={btn}")
for persona in out: print(persona,'errors:',out[persona]['_errors'])
