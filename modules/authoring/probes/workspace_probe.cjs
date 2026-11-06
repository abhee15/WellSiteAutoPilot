// Optional local browser probe. Requires Playwright and a Chromium executable; no product dependency added.
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const {execFileSync}=require('node:child_process');
const { chromium } = require(process.env.WSA_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../../..');
const output = path.resolve(process.argv[2] || path.join(__dirname, '../artifacts/workspace'));
(async () => {
 await fs.mkdir(output,{recursive:true});
 const server = http.createServer(async(req,res)=>{
  const file = path.resolve(root,'.'+decodeURIComponent(new URL(req.url,'http://localhost').pathname));
  if(!file.startsWith(root+path.sep)){res.writeHead(403);res.end();return;}
  try{const bytes=await fs.readFile(file);const ext=path.extname(file);res.setHeader('Content-Type',({'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.json':'application/json'})[ext]||'application/octet-stream');res.end(bytes);}catch{res.writeHead(404);res.end();}
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const results=[];
 try {
  for(const width of [1440,390,320]){
   const browser=await chromium.launch({executablePath:process.env.WSA_CHROMIUM_PATH,args:['--no-sandbox','--single-process','--no-zygote','--disable-gpu']});
   try {
    const page=await browser.newPage({viewport:{width,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/modules/authoring/probes/workspace.html`);
    const status=page.getByRole('status');
    await status.filter({hasText:'Fixture ready'}).waitFor();
    assert.equal(await page.getByRole('button',{name:'Probe build',exact:true}).isEnabled(),true); // Build does not require executing a draft.
    await page.getByRole('button',{name:'Probe build',exact:true}).click();await status.filter({hasText:'Simulated build succeeded'}).waitFor();
    const initialDownload=page.waitForEvent('download');await page.getByRole('button',{name:'Export probe report'}).click();const initial=await initialDownload;const initialPath=path.join(output,`untested-${width}.json`);await initial.saveAs(initialPath);const initialData=JSON.parse(await fs.readFile(initialPath,'utf8'));assert.equal(initialData.calibrated,null);assert.equal(initialData.testStatus,'not-run');
    const calibrate=page.getByRole('button',{name:/Calibrate load/});await calibrate.focus();await page.keyboard.press('Enter');
    await page.getByRole('heading',{name:'Calibrate load',exact:true}).waitFor();
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    assert.match(await page.locator('#trace').innerText(),/Peak load\s+490/);assert.match(await page.locator('#trace').innerText(),/Stub prediction\s+392/);
    await page.getByText('Structured calibrated curves',{exact:true}).click();
    assert.match(await page.locator('#curves').innerText(),/capturedAtUtc/);
    await page.getByRole('button',{name:'Probe build',exact:true}).click();await status.filter({hasText:'Simulated build succeeded'}).waitFor();
    assert.equal(await page.getByRole('button',{name:'Export probe report'}).isEnabled(),true);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    await page.getByRole('button',{name:'Save calculation',exact:true}).click();
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    assert.match(await page.locator('#trace').innerText(),/Load difference\s+98/);
    await page.getByLabel('Right dependency').selectOption('margin');await page.getByRole('button',{name:'Save calculation',exact:true}).click();
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    assert.match(await page.locator('#trace').innerText(),/Load difference\s+398/);
    await page.getByLabel('Left dependency').selectOption('custom-1');await page.getByRole('button',{name:'Save calculation',exact:true}).click();
    await page.getByRole('alert').filter({hasText:'PROBE_DEPENDENCY_CYCLE'}).waitFor();
    await page.getByLabel('Left dependency').selectOption('peak');await page.getByRole('button',{name:'Save calculation',exact:true}).click();
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    await page.getByRole('button',{name:'Probe build',exact:true}).click();await status.filter({hasText:'Simulated build succeeded'}).waitFor();
    const downloaded=page.waitForEvent('download');await page.getByRole('button',{name:'Export probe report'}).click();
    const download=await downloaded, exported=path.join(output,`export-${width}.json`), roundtrip=path.join(output,`console-${width}.json`);await download.saveAs(exported);
    const parsed=JSON.parse(await fs.readFile(exported,'utf8'));assert.equal(parsed.authoringDocument.nodes[0].right,'margin');
    execFileSync(process.execPath,[path.join(__dirname,'document_probe.mjs'),path.join(output,`console-report-${width}.json`),exported,roundtrip]);
    await page.getByLabel('Import probe document').setInputFiles(roundtrip);await status.filter({hasText:'Document imported'}).waitFor();
    assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    assert.match(await page.locator('#trace').innerText(),/Load difference\s+398/);
    await page.screenshot({path:path.join(output,`workspace-${width}.png`),fullPage:true});
    await page.getByLabel('Load scale',{exact:true}).fill('1');
    assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);assert.equal(await page.locator('#trace').innerText(),'');
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    assert.match(await page.locator('#trace').innerText(),/Peak load\s+250/);assert.match(await page.locator('#trace').innerText(),/No adjustment/);
    await page.getByLabel('Outcome condition').selectOption('nonpositive');await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();assert.match(await page.locator('#trace').innerText(),/Review adjustment/);
    await page.getByText('Calculation implementation',{exact:true}).click();
    assert.match(await page.locator('#condition-formula').innerText(),/margin <= 0/);
    await page.getByRole('button',{name:'Probe build',exact:true}).click();await status.filter({hasText:'Simulated build succeeded'}).waitFor();
    const nondefaultDownload=page.waitForEvent('download');await page.getByRole('button',{name:'Export probe report'}).click();const nondefault=await nondefaultDownload;const changedPath=path.join(output,`changed-${width}.json`), changedConsole=path.join(output,`changed-console-${width}.json`);await nondefault.saveAs(changedPath);
    execFileSync(process.execPath,[path.join(__dirname,'document_probe.mjs'),path.join(output,`changed-report-${width}.json`),changedPath,changedConsole]);
    const consoleEvidence=JSON.parse(await fs.readFile(path.join(output,`changed-report-${width}.json`),'utf8'));assert.equal(consoleEvidence.importedEvidence.base.peak.value,250);assert.equal(consoleEvidence.importedEvidence.outputs[0].value,350);
    await page.getByLabel('Import probe document').setInputFiles(changedConsole);await status.filter({hasText:'Document imported'}).waitFor();await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();assert.match(await page.locator('#trace').innerText(),/Load difference\s+350/);
    await page.getByRole('button',{name:'Test fixture',exact:true}).click();await page.getByLabel('Load offset (lbf)').fill('20');
    await status.filter({hasText:'Discarded test result'}).waitFor();assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);
    await page.getByLabel('Fixture case').selectOption('broken');await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'CARD_SAMPLES_INVALID'}).waitFor();
    assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);
    await page.getByLabel('Fixture case').selectOption('good');await page.getByRole('button',{name:'Test fixture',exact:true}).click();await status.filter({hasText:'Test passed'}).waitFor();
    await page.getByLabel('Probe build outcome').selectOption('failure');await page.getByRole('button',{name:'Probe build',exact:true}).click();await status.filter({hasText:'Simulated compiler failure'}).waitFor();
    assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);
    await page.getByLabel('Probe build outcome').selectOption('success');await page.getByRole('button',{name:'Probe build',exact:true}).click();await page.getByRole('button',{name:'Cancel',exact:true}).click();
    await page.waitForTimeout(250);assert.match(await status.innerText(),/Cancelled/);assert.equal(await page.getByRole('button',{name:'Export probe report'}).isDisabled(),true);
    assert.deepEqual(errors,[]);
    results.push({width,status:'passed',checks:['Enter activates a focused outline button','reference fixture arithmetic','structured JSON metadata displayed','dependent step creation and edge editing','cycle rejection','condition editing','browser export-console import-export-browser import','build permitted without executing tests','parameter-dependent recomputation','stale test discarded','mismatched arrays rejected','build failure','cancellation','export invalidation','no horizontal overflow','no page errors']});
   } finally {await browser.close();}
  }
  await fs.writeFile(path.join(output,'report.json'),JSON.stringify({probe:'disposable-calculation-workspace',status:'passed',results,notEstablished:['Production React integration','Product graph-to-code compilation or existing CLI compatibility','Arbitrary-code or decimal algorithm equivalence','Authenticated live preview or download','Hosted build/test isolation','Live providers']},null,2)+'\n');
  console.log(JSON.stringify({status:'passed',viewports:results.length,report:path.join(output,'report.json')}));
 } finally {await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
