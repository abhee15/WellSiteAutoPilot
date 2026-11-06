// Browser/console transport of one candidate document; evaluation always uses the same .NET probe.
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),assert=require('node:assert/strict');
const {execFileSync}=require('node:child_process');
const dll=path.resolve(__dirname,'composition/bin/Release/net10.0/CompositionProbe.dll');
(async()=>{
 const stage=await fs.mkdtemp(path.join(os.tmpdir(),'decimal-conformance-'));let browser;
 try{
 const doc={profile:'probe-decimal-v1',inputs:[{id:'a',literal:'0.3',unit:'lbf'},{id:'b',literal:'0.1',unit:'lbf'}],nodes:[{id:'ratio',operation:'ratio',left:'a',right:'b'},{id:'delta',operation:'difference',left:'a',right:'b'}]};
 async function evaluate(document){const file=path.join(stage,'candidate.json');await fs.writeFile(file,JSON.stringify(document));return JSON.parse(execFileSync(process.env.WSA_DOTNET||'dotnet',[dll,'--document',file],{encoding:'utf8',timeout:15000}));}
 const checks=[];const record=(name,condition)=>{assert.ok(condition,name);checks.push(name);};
 const initial=await evaluate(doc);record('Authoritative decimal ratio is exactly 3',initial.outputs[0].literal==='3');record('Authoritative decimal difference is exactly 0.2',initial.outputs[1].literal==='0.2');
 const precise=structuredClone(doc);precise.inputs[0].literal='0.1234567890123456789012345678';precise.inputs[1].literal='0.0000000000000000000000000001';precise.nodes=[{id:'delta',operation:'difference',left:'a',right:'b'}];
 const exact=await evaluate(precise);record('28-digit decimal literal evaluates without browser Number conversion',exact.outputs[0].literal==='0.1234567890123456789012345677');
 for(const [name,mutate,code] of [
 ['zero denominator',d=>d.inputs[1].literal='0','DIVIDE_BY_ZERO'],
 ['overflow',d=>{d.inputs[0].literal='79228162514264337593543950335';d.inputs[1].literal='-1';d.nodes=[d.nodes[1]];},'DECIMAL_OVERFLOW'],
 ['numeric JSON instead of decimal text',d=>d.inputs[0].literal=0.3,'DECIMAL_TEXT_REQUIRED'],
 ['unit mismatch',d=>d.inputs[1].unit='psi','UNIT_MISMATCH'],
 ['cycle',d=>d.nodes[0].left='ratio','DEPENDENCY_CYCLE']
 ]){const candidate=structuredClone(doc);mutate(candidate);record('Reject '+name,(await evaluate(candidate)).code===code);}
 if(process.env.WSA_CHROMIUM_PATH){
 const {chromium}=require(process.env.WSA_PLAYWRIGHT_MODULE||'playwright');browser=await chromium.launch({executablePath:process.env.WSA_CHROMIUM_PATH,args:['--no-sandbox','--single-process','--no-zygote','--disable-gpu']});
 const page=await browser.newPage();await page.setContent('<label>Decimal literal<input id="literal" type="text"></label><button id="export">Export candidate</button><pre id="result"></pre>');
 await page.evaluate(document=>{window.candidate=document;document=null;window.document.getElementById('literal').value=window.candidate.inputs[0].literal;window.document.getElementById('export').onclick=()=>{window.candidate.inputs[0].literal=window.document.getElementById('literal').value;const a=window.document.createElement('a');a.href=URL.createObjectURL(new Blob([JSON.stringify(window.candidate)],{type:'application/json'}));a.download='candidate.json';a.click();};},precise);
 await page.getByLabel('Decimal literal').fill('0.2234567890123456789012345678');const downloadEvent=page.waitForEvent('download');await page.getByRole('button',{name:'Export candidate'}).click();const download=await downloadEvent;const file=path.join(stage,'browser.json');await download.saveAs(file);const imported=JSON.parse(await fs.readFile(file,'utf8'));
 record('Browser text edit/download preserves all decimal digits',imported.inputs[0].literal==='0.2234567890123456789012345678');const authoritative=await evaluate(imported);
 record('Browser-exported document evaluated by .NET exactly',authoritative.outputs[0].literal==='0.2234567890123456789012345677');
 const consoleRoundtrip=JSON.parse(JSON.stringify(authoritative.document));record('Console export preserves the entire browser document',JSON.stringify(consoleRoundtrip)===JSON.stringify(imported));
 await page.evaluate(result=>{window.document.getElementById('result').textContent=JSON.stringify(result);},authoritative);
 record('Browser renders exact authoritative result as text',(await page.locator('#result').textContent()).includes('0.2234567890123456789012345677'));
 const bad=structuredClone(imported);bad.inputs[0].literal=0.2;const rejected=await evaluate(bad);await page.evaluate(r=>window.document.getElementById('result').textContent=JSON.stringify(r),rejected);record('Browser and console share the authoritative rejection code',(await page.locator('#result').textContent()).includes('DECIMAL_TEXT_REQUIRED'));
 }
 const report={probe:'authoritative-decimal-document-conformance',status:'passed',checks,browserExecuted:Boolean(browser),notEstablished:['Existing product CLI or React editor integration','General graph compilation and SDK-code import','Hosted isolation or authenticated preview/delivery','Arbitrary numeric profiles and unit conversion']};
 const output=process.argv[2];if(output){await fs.mkdir(path.dirname(output),{recursive:true});await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');}console.log(JSON.stringify(report));
 }finally{if(browser)await browser.close();await fs.rm(stage,{recursive:true,force:true});}
})().catch(error=>{console.error(error);process.exitCode=1;});
