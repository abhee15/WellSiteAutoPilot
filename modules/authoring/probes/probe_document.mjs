// Candidate document semantics ONLY for this disposable probe. Never eval arbitrary expressions.
export function evaluateDependentSteps(nodes, inputs) {
 if (!Array.isArray(nodes) || nodes.length > 16) throw Error('PROBE_NODE_LIMIT');
 const byId = new Map(), values = new Map(Object.entries(inputs)), visiting = new Set();
 for (const node of nodes) {
  if (!node || typeof node !== 'object' || typeof node.id !== 'string' || !/^[a-z][a-z0-9-]{0,63}$/.test(node.id) || byId.has(node.id) || values.has(node.id)) throw Error('PROBE_DUPLICATE_OR_INVALID_ID');
  if (typeof node.name !== 'string' || !node.name.trim() || node.name.length>128) throw Error('PROBE_NAME_INVALID');
  if (!['difference','ratio'].includes(node.operation)) throw Error('PROBE_OPERATION_UNSUPPORTED');
  byId.set(node.id,node);
 }
 function visit(id) {
  if(values.has(id)) return values.get(id);
  const node=byId.get(id);if(!node)throw Error('PROBE_DEPENDENCY_MISSING');if(visiting.has(id))throw Error('PROBE_DEPENDENCY_CYCLE');
  visiting.add(id);const left=visit(node.left),right=visit(node.right);
  if(left.unit!==right.unit)throw Error('PROBE_UNIT_MISMATCH');
  if(node.operation==='ratio' && right.value===0)throw Error('PROBE_DIVIDE_BY_ZERO');
  const value=node.operation==='difference'?left.value-right.value:left.value/right.value;
  if(!Number.isFinite(value))throw Error('PROBE_NUMBER_INVALID');
  const result={value,unit:node.operation==='ratio'?'1':left.unit};values.set(id,result);visiting.delete(id);return result;
 }
 for(const node of nodes)visit(node.id);
 return nodes.map(node=>({...node,...values.get(node.id)}));
}
export function validateDocument(document) {
 if(document?.schemaVersion!==1 || document.numberSemantics!=='probe-js-finite-double')throw Error('PROBE_DOCUMENT_UNSUPPORTED');
 if(document.opaqueStep?.moduleId!=='sample.card-calibration' || document.opaqueStep.version!=='1.0.0')throw Error('PROBE_OPAQUE_STEP_UNSUPPORTED');
 if(!['positive','nonpositive'].includes(document.condition))throw Error('PROBE_CONDITION_UNSUPPORTED');
 evaluateDependentSteps(document.nodes,{peak:{value:490,unit:'lbf'},prediction:{value:392,unit:'lbf'},margin:{value:92,unit:'lbf'}});
 return document;
}

export function referenceFixtureValues(cards, parameters) {
 const {scale,offset,factor,threshold}=parameters??{};
 if([scale,offset,factor,threshold].some(value=>typeof value!=='number'||!Number.isFinite(value)))throw Error('PROBE_PARAMETERS_INVALID');
 if(scale<0.1||scale>10||offset< -10000||offset>10000||factor<0||factor>1||threshold<0||threshold>1000000)throw Error('PROBE_PARAMETERS_RANGE');
 if(!Array.isArray(cards)||!cards.length||cards.some(card=>!Array.isArray(card.load)||!card.load.length||card.load.length!==card.position?.length))throw Error('CARD_SAMPLES_INVALID: paired load/position arrays differ');
 const calibrated=cards.map(card=>({...card,load:card.load.map(value=>value*scale+offset)}));
 const peak=Math.max(...calibrated.flatMap(card=>card.load));const prediction=peak*factor,margin=prediction-threshold;
 if([peak,prediction,margin].some(value=>!Number.isFinite(value)))throw Error('PROBE_NUMBER_INVALID');
 return {calibrated,peak,prediction,margin};
}
