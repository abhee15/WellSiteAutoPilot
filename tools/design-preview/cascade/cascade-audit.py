"""Cascade audit: finds declarations that tie on specificity across different CSS files, so the winner is decided by load order.

Usage (see README): build unminified, split the bundle per source file, serve the app (mock server), then
  python3 cascade-audit.py segments.json [chromium-path]
Exits 1 on any conflict not in ALLOW. Each ALLOW entry needs a reason; prefer fixing the selector.
"""
import json, re, sys
from playwright.sync_api import sync_playwright
BASE='http://127.0.0.1:5199'
SEG=json.load(open(sys.argv[1] if len(sys.argv)>1 else 'segments.json'))
CHROMIUM=sys.argv[2] if len(sys.argv)>2 else None
# Reviewed and harmless: both sides resolve to the same rendered value (or the difference is intentional).
ALLOW=['.asset-view-bar .secondary-button {var(--font-size-body-small)}','.connection-field input {var(--font-weight-regular)}']
JS=r"""
(segs)=>{
 const split=s=>{const out=[];let d=0,cur='';for(const ch of s){if(ch=='('||ch=='[')d++;if(ch==')'||ch==']')d--;if(ch==','&&d==0){out.push(cur.trim());cur='';}else cur+=ch;}if(cur.trim())out.push(cur.trim());return out;};
 const spec=sel=>{ // returns [a,b,c]
  let a=0,b=0,c=0; let s=sel;
  // functional pseudos
  s=s.replace(/:(where)\((?:[^()]|\([^()]*\))*\)/g,'');
  s=s.replace(/:(is|not|has|matches)\(((?:[^()]|\([^()]*\))*)\)/g,(m,f,inner)=>{let best=[0,0,0];for(const p of split(inner)){const q=spec(p);if(q[0]>best[0]||(q[0]==best[0]&&(q[1]>best[1]||(q[1]==best[1]&&q[2]>best[2]))))best=q;}a+=best[0];b+=best[1];c+=best[2];return '';});
  s=s.replace(/::[\w-]+/g,()=>{c++;return '';});
  s=s.replace(/\[[^\]]*\]/g,()=>{b++;return '';});
  s=s.replace(/#[\w-]+/g,()=>{a++;return '';});
  s=s.replace(/\.[\w-]+/g,()=>{b++;return '';});
  s=s.replace(/:[\w-]+(\([^)]*\))?/g,()=>{b++;return '';});
  s=s.replace(/(^|[\s>+~])([a-zA-Z][\w-]*)/g,()=>{c++;return ' ';});
  return [a,b,c];
 };
 const cmp=(x,y)=>x[0]-y[0]||x[1]-y[1]||x[2]-y[2];
 // build rule list
 const rules=[];let order=0;
 const walk=(list,file,ctx)=>{for(const r of list){
   if(r.type===1){for(const sel of split(r.selectorText)){ if(/::/.test(sel)) continue; rules.push({file,sel,sp:spec(sel),style:r.style,order:order++,ctx});}}
   else if(r.type===4){ if(matchMedia(r.conditionText||r.media.mediaText).matches) walk(r.cssRules,file,ctx+'@media '+r.media.mediaText);}
   else if(r.constructor.name==='CSSContainerRule'){ walk(r.cssRules,file,ctx+'@container '+r.conditionText);}
   else if(r.type===12){ if(CSS.supports(r.conditionText)) walk(r.cssRules,file,ctx);}
   else if(r.cssRules&&r.constructor.name==='CSSLayerBlockRule'){walk(r.cssRules,file,ctx);}
 }};
 for(const ss of document.styleSheets){const f=ss.ownerNode&&ss.ownerNode.dataset&&ss.ownerNode.dataset.file; if(!f) continue; walk(ss.cssRules,f,'');}
 const res={};
 for(const el of document.querySelectorAll('body, body *')){
  const per={};
  for(const r of rules){ let m=false; try{m=el.matches(r.sel);}catch(e){} if(!m)continue;
   if(r.ctx.includes('@container')){ /* can't evaluate; assume applies only if element currently shows effect: skip */ }
   for(let i=0;i<r.style.length;i++){const p=r.style[i];if(p.startsWith('--'))continue;
    const imp=r.style.getPropertyPriority(p)==='important';
    (per[p]=per[p]||[]).push({file:r.file,sel:r.sel,sp:r.sp,v:r.style.getPropertyValue(p),imp,order:r.order,ctx:r.ctx});}
  }
  for(const p in per){const L=per[p];if(L.length<2)continue;
   const top=L.filter(x=>x.imp);const pool=top.length?top:L;
   let mx=pool[0].sp;for(const x of pool)if(cmp(x.sp,mx)>0)mx=x.sp;
   const tied=pool.filter(x=>cmp(x.sp,mx)===0);
   if(tied.length<2)continue;
   const files=new Set(tied.map(x=>x.file));const vals=new Set(tied.map(x=>x.v));
   if(files.size<2||vals.size<2)continue;
   tied.sort((x,y)=>x.order-y.order);const win=tied[tied.length-1];
   for(const lo of tied.slice(0,-1)){ if(lo.file===win.file||lo.v===win.v)continue;
     const k=p+' | WIN '+win.sel+' {'+win.v+'} ['+win.file+']  OVER  '+lo.sel+' {'+lo.v+'} ['+lo.file+'] '+(lo.ctx||win.ctx?'ctx:'+lo.ctx+'/'+win.ctx:'');
     res[k]=(res[k]||0)+1; }
  }
 }
 return res;
};
"""
SCEN=[('#overview',[]),('#operations',[]),('#assets',[]),('#assets',[('tab','Asset types')]),('#assets',[('tab','Views')]),('#assets',[('btn','Filters')]),
 ('#logic',[]),('#logic',[('btn','Filters')]),('#logic',[('tab','Modules')]),('#logic',[('btn','Configure Logic')]),('#logic',[('btn','Inspect revisions')]),
 ('#connections',[]),('#connections',[('btn','ForeSite')]),('#recommendations',[]),('#status',[]),('#access',[]),('#access',[('tab','Roles and permissions')]),('#access',[('tab','Activity')]),('#operations',[('btn','Inspect execution')]),('#assets',[('btn','Open Well 7-12H')])]
agg={}
with sync_playwright() as p:
    b=p.chromium.launch(**({'executable_path':CHROMIUM} if CHROMIUM else {}))
    for vw in (1440,420):
        ctx=b.new_context(viewport={'width':vw,'height':900}); ctx.add_cookies([{'name':'persona','value':'Admin','url':BASE}])
        pg=ctx.new_page()
        for hsh,acts in SCEN:
            pg.goto(BASE+'/'); pg.goto(BASE+'/'+hsh); pg.wait_for_timeout(700)
            pg.evaluate("""(segs)=>{document.querySelectorAll('link[rel=stylesheet]').forEach(l=>l.remove());
              for(const s of segs){const e=document.createElement('style');e.dataset.file=s.file;e.textContent=s.css;document.head.appendChild(e);}}""",SEG)
            for kind,name in acts:
                try:
                    pg.get_by_role('tab' if kind=='tab' else 'button',name=re.compile(name)).first.click(timeout=1200); pg.wait_for_timeout(400)
                except Exception: pass
            r=pg.evaluate(JS,SEG)
            for k,v in r.items(): agg.setdefault(k,set()).add(f'{hsh}@{vw}')
        ctx.close()
bad={k:v for k,v in agg.items() if not any(a in k for a in ALLOW)}
for k in sorted(bad): print(k,'\n    seen in:',', '.join(sorted(bad[k])))
print(len(bad),'unreviewed order-dependent conflicts')
sys.exit(1 if bad else 0)
