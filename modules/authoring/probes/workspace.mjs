import {evaluateDependentSteps,validateDocument,referenceFixtureValues} from './probe_document.mjs';
// Disposable, fixture-only interaction model. Not a product API or workflow runtime.
const byId = id => document.getElementById(id);
let cards, fixtureHash, testOutcome='not-run', revision = 0, tested = -1, built = -1, activeJob = 0, building=false;
let documentModel = {schemaVersion:1, numberSemantics:'probe-js-finite-double',opaqueStep:{moduleId:'sample.card-calibration',version:'1.0.0'},condition:'positive',nodes:[]};
const baseInputs = {peak:{value:490,unit:'lbf'},prediction:{value:392,unit:'lbf'},margin:{value:92,unit:'lbf'}};
const status = message => { byId('status').textContent = message; };
const descriptions = {
 sources: 'Cards: ForeSite source-A, external ID 597, revision 3. Calibration: ForeSite source-B, external ID 597, revision 8. These are distinct bindings to an explicitly mapped logical well. Calculation: WAMI-shaped synthetic stub, revision 2, model synthetic-v1. No live access.',
 calibrate: 'Transform every load sample using scale and offset. Preserve paired positions and each card’s identity, units and capture time. Reject misaligned arrays.',
 peak: 'Aggregate per-card maxima into one peak load. The output depends on every calibrated curve, not a separately entered constant.',
 predict: 'Pass the peak result into a separately named calculation dependency. The stub multiplies by a factor; it performs no engineering physics or network calls.',
 margin: 'Subtract the configured threshold from prediction. Inspect the intermediate margin before selecting an outcome.',
 proposal: 'The selected outcome condition chooses operator review or no adjustment. This test records a proposed outcome only. No source-system write is available.'
};
function controls() {
 let valid=Boolean(cards);try{values();validateDocument(documentModel);}catch{valid=false;}
 byId('build').disabled = building || !valid; byId('download').disabled = built !== revision;
}
function clearEvidence(message) {
 tested = built = -1; testOutcome=message==='testing'?'running':message==='cancelled'?'cancelled':'stale'; byId('trace').replaceChildren(); byId('curves').textContent = 'No current test evidence';
 byId('revision').textContent = `Draft revision ${revision} · ${message}`; controls();
}
for (const node of document.querySelectorAll('[data-step]')) node.addEventListener('click', () => {
 document.querySelectorAll('[data-step]').forEach(button => button.setAttribute('aria-pressed', String(button === node)));
 byId('detail-title').textContent = node.firstChild.textContent; byId('description').textContent = descriptions[node.dataset.step];
});
byId('description').textContent = descriptions.sources;
for (const id of ['scale','offset','factor','threshold','case']) byId(id).addEventListener('input', () => {
 revision++; clearEvidence('upstream edit — test required'); status('Results and build invalidated by an upstream edit.');
});
function values() {
 const ids = ['scale','offset','factor','threshold'];
 for (const id of ids) if (!byId(id).checkValidity() || !Number.isFinite(byId(id).valueAsNumber)) throw Error(`Invalid ${id}`);
 return ids.map(id => byId(id).valueAsNumber);
}
const wait = () => new Promise(resolve => setTimeout(resolve, 180));
byId('test').addEventListener('click', async () => {
 const started = revision, job = ++activeJob; building=true; clearEvidence('testing'); status('Testing fixture…');
 byId('cancel').hidden = false; byId('test').disabled = true;
 await wait();
 if (job !== activeJob) return;
 try {
  if (started !== revision) throw Error('Discarded test result: draft changed while testing');
  const [scale, offset, factor, threshold] = values();
  const fixture = structuredClone(cards);
  if (byId('case').value === 'broken') fixture[0].position.pop();
  const {calibrated,peak,prediction,margin}=referenceFixtureValues(fixture,{scale,offset,factor,threshold});
  const review = documentModel.condition==='positive' ? margin>0 : margin<=0;
  const custom=evaluateDependentSteps(documentModel.nodes,{peak:{value:peak,unit:'lbf'},prediction:{value:prediction,unit:'lbf'},margin:{value:margin,unit:'lbf'}});
  const rows = [['Calibrated card count',calibrated.length,'cards, scale, offset'],['Peak load',peak,'calibrated curves'],['Stub prediction',prediction,'peak, stub factor'],['Margin',margin,'prediction, threshold'],['Outcome',review ? 'Review adjustment' : 'No adjustment','margin']];
  rows.push(...custom.map(node=>[node.name,node.value,`${node.left}, ${node.right} (${node.unit})`]));
  for (const row of rows) { const tr = document.createElement('tr'); for (const value of row) {const td = document.createElement('td');td.textContent=String(value);tr.append(td);} byId('trace').append(tr); }
  byId('curves').textContent = JSON.stringify(calibrated, null, 2); tested = revision; testOutcome='passed';
  byId('revision').textContent = `Draft revision ${revision} · tested with synthetic fixtures`;
  status('Test passed. Intermediate values and final outcome are available.');
 } catch (error) { testOutcome=started===revision?'failed':'stale'; status(error.message); }
 finally { building=false; byId('cancel').hidden = true; byId('test').disabled = false; controls(); }
});
byId('build').addEventListener('click', async () => {
 try{values();validateDocument(documentModel);}catch{return;}
 const started = revision, job = ++activeJob; built = -1; building=true; status('Simulating build…');
 byId('build').disabled = true; byId('test').disabled = true; byId('download').disabled = true; byId('cancel').hidden = false;
 await wait();
 if (job !== activeJob) return;
 if (started !== revision) status('Discarded build: draft changed');
 else if (byId('build-case').value === 'failure') status('Simulated compiler failure. No artifact is available.');
 else { built = revision; status('Simulated build succeeded. Probe report export available; this is not a module package.'); }
 building=false; byId('test').disabled=false; byId('cancel').hidden = true; controls();
});
byId('cancel').addEventListener('click', () => { activeJob++; building=false; tested = built = -1; byId('cancel').hidden = true; byId('test').disabled = false; clearEvidence('cancelled'); status('Cancelled. No current result or artifact.'); });
byId('download').addEventListener('click', () => {
 if (built !== revision) return;
 const blob = new Blob([JSON.stringify({probe:true,revision,authoringDocument:{...documentModel,fixtureCase:byId('case').value,parameters:{scale:byId('scale').valueAsNumber,offset:byId('offset').valueAsNumber,factor:byId('factor').valueAsNumber,threshold:byId('threshold').valueAsNumber}},trace:byId('trace').innerText,testStatus:testOutcome,fixture:{id:'repository-synthetic-cards',sha256:fixtureHash,case:byId('case').value},calibrated:tested===revision?JSON.parse(byId('curves').textContent):null},null,2)],{type:'application/json'});
 const url = URL.createObjectURL(blob), link = document.createElement('a');link.href=url;link.download='authoring-probe-report.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),0);
});
try { const response = await fetch('../../samples/structured-cards/cards.json'); if (!response.ok) throw Error('Fixture unavailable');const raw=await response.text();cards=JSON.parse(raw);fixtureHash=[...new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(raw)))].map(value=>value.toString(16).padStart(2,'0')).join('');byId('test').disabled=false;controls();status('Fixture ready. Revisit sources, calculations and properties in any order.'); }
catch(error) {status(error.message);}

function editChanged(message) { revision++;clearEvidence(message);status(message); }
function renderNodes() {
 const previousEdit=byId('edit-node').value;
 byId('edit-node').replaceChildren(new Option('New calculation','new'));
 byId('custom-nodes').replaceChildren();
 for(const node of documentModel.nodes){
  byId('edit-node').append(new Option(node.name,node.id));
  const li=document.createElement('li');li.textContent=`${node.id}: ${node.name} = ${node.left} ${node.operation} ${node.right}`;byId('custom-nodes').append(li);
 }
 for(const id of ['node-left','node-right']){
  const old=byId(id).value;byId(id).replaceChildren(...Object.keys(baseInputs).map(key=>new Option(key,key)),...documentModel.nodes.map(node=>new Option(node.name,node.id)));
  if([...byId(id).options].some(option=>option.value===old))byId(id).value=old;
 }
 if(documentModel.nodes.some(node=>node.id===previousEdit))byId('edit-node').value=previousEdit;
}
byId('edit-node').addEventListener('change',()=>{
 const node=documentModel.nodes.find(node=>node.id===byId('edit-node').value);if(!node)return;
 byId('node-label').value=node.name;byId('node-operation').value=node.operation;byId('node-left').value=node.left;byId('node-right').value=node.right;
});
byId('save-node').addEventListener('click',()=>{
 try{
  const editing=byId('edit-node').value;
  const node={id:editing==='new'?`custom-${Math.max(0,...documentModel.nodes.map(n=>Number(n.id.split('-').at(-1))||0))+1}`:editing,name:byId('node-label').value,operation:byId('node-operation').value,left:byId('node-left').value,right:byId('node-right').value};
  const candidate={...documentModel,nodes:editing==='new'?[...documentModel.nodes,node]:documentModel.nodes.map(n=>n.id===editing?node:n)};
  validateDocument(candidate);documentModel=candidate;byId('node-error').textContent='';renderNodes();byId('edit-node').value=node.id;editChanged('Calculation dependency changed — test evidence invalidated.');
 }catch(error){byId('node-error').textContent=error.message;byId('save-node').focus();}
});
byId('condition').addEventListener('change',()=>{documentModel.condition=byId('condition').value;byId('condition-formula').textContent=documentModel.condition==='positive'?"outcome = margin > 0 ? 'Review adjustment' : 'No adjustment'":"outcome = margin <= 0 ? 'Review adjustment' : 'No adjustment'";descriptions.proposal=`The selected condition is ${documentModel.condition}. Test outcomes are captured only; no source-system write is available.`;editChanged('Outcome condition changed — test evidence invalidated.');});
byId('import-document').addEventListener('change',async event=>{
 try{
  const file=event.target.files[0];if(!file||file.size>65536)throw Error('PROBE_DOCUMENT_SIZE');
  const imported=JSON.parse(await file.text());const candidate=validateDocument(imported.authoringDocument??imported);
  const parameters=candidate.parameters;if(!parameters||['scale','offset','factor','threshold'].some(id=>typeof parameters[id]!=='number'||!Number.isFinite(parameters[id])))throw Error('PROBE_PARAMETERS_INVALID');
  documentModel=candidate;for(const id of ['scale','offset','factor','threshold'])byId(id).value=String(parameters[id]);
  byId('condition').value=documentModel.condition;byId('case').value=candidate.fixtureCase??'good';byId('condition-formula').textContent=documentModel.condition==='positive'?"outcome = margin > 0 ? 'Review adjustment' : 'No adjustment'":"outcome = margin <= 0 ? 'Review adjustment' : 'No adjustment'";renderNodes();editChanged('Document imported — validation and test evidence must be renewed.');
 }catch(error){status(error.message);}
});
