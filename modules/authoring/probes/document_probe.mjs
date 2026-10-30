import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {evaluateDependentSteps,validateDocument,referenceFixtureValues} from './probe_document.mjs';
const inputs={peak:{value:490,unit:'lbf'},prediction:{value:392,unit:'lbf'},margin:{value:92,unit:'lbf'}};
const document={schemaVersion:1,numberSemantics:'probe-js-finite-double',opaqueStep:{moduleId:'sample.card-calibration',version:'1.0.0'},condition:'positive',nodes:[{id:'custom-1',name:'Load difference',operation:'difference',left:'peak',right:'prediction'},{id:'custom-2',name:'Normalized difference',operation:'ratio',left:'custom-1',right:'peak'}]};
const output=evaluateDependentSteps(validateDocument(document).nodes,inputs);
assert.equal(output[0].value,98);assert.equal(output[1].unit,'1');
const imported=JSON.parse(JSON.stringify(document));assert.deepEqual(evaluateDependentSteps(imported.nodes,inputs),output);
assert.deepEqual(imported.opaqueStep,document.opaqueStep);
const invalid=[
 [{...document.nodes[0],id:undefined}],
 [{...document.nodes[0],id:null}],
 [{...document.nodes[0],left:'missing'}],
 [{...document.nodes[0],left:'custom-1'}],
 [{...document.nodes[0]}, {...document.nodes[0]}],
 [{...document.nodes[0],operation:'eval'}],
 [{...document.nodes[0],left:'custom-2'}, {...document.nodes[1],left:'custom-1'}]
];
for(const nodes of invalid)assert.throws(()=>evaluateDependentSteps(nodes,inputs));
assert.throws(()=>evaluateDependentSteps([{...document.nodes[0],operation:'ratio',right:'zero'}],{...inputs,zero:{value:0,unit:'lbf'}}),/DIVIDE_BY_ZERO/);
assert.throws(()=>evaluateDependentSteps([{...document.nodes[0],right:'pressure'}],{...inputs,pressure:{value:1,unit:'psi'}}),/UNIT_MISMATCH/);
let importedEvidence=null;
if(process.argv[3]){
 const saved=JSON.parse(await readFile(process.argv[3],'utf8'));const imported=validateDocument(saved.authoringDocument??saved);
 const cards=JSON.parse(await readFile(new URL('../../samples/structured-cards/cards.json',import.meta.url),'utf8'));
 const values=referenceFixtureValues(cards,imported.parameters);
 const base={peak:{value:values.peak,unit:'lbf'},prediction:{value:values.prediction,unit:'lbf'},margin:{value:values.margin,unit:'lbf'}};
 const outputs=evaluateDependentSteps(imported.nodes,base);importedEvidence={parameters:imported.parameters,base,outputs};
 if(saved.calibrated)assert.deepEqual(values.calibrated,saved.calibrated);
 if(process.argv[4])await writeFile(process.argv[4],JSON.stringify(imported,null,2)+'\n');
}
const report={probe:'candidate-dependent-calculation-document',status:'passed',checks:13,document,output,importedEvidence,notEstablished:['Product graph compiler or manifest compatibility','Arbitrary source import; opaque code step identity is preserved only','Console CLI/web equivalence for decimal algorithms','Production validation or routing']};
if(process.argv[2])await writeFile(process.argv[2],JSON.stringify(report,null,2)+'\n');
console.log('13 candidate-document assertions passed');
