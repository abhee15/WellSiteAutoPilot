import test from 'node:test';
import assert from 'node:assert/strict';
import {
  addStep, buildOutline, documentsEqual, readDocument, removeElement, selectionForIssuePath, usedBy, usesOf,
} from '../src/views/authoring.ts';

const doc = () => readDocument({
  schema: 'x', profile: 'p', module: { moduleId: 'm', version: '1.0.0', displayName: 'M', publisher: 'P' },
  inputs: [{ id: 'level', label: 'Level', quantity: 'q', assetRole: 'asset' }],
  parameters: [{ id: 'limit', label: 'Limit', defaultValue: '10' }],
  steps: [{ id: 'gap', label: 'Gap', kind: 'calculation', operation: 'subtract',
    operands: [{ kind: 'parameter', ref: 'limit' }, { kind: 'input', ref: 'level' }] }],
  conditions: [{ id: 'low', label: 'Low', left: { kind: 'step', ref: 'gap' }, comparator: 'gt', right: { kind: 'literal', value: '0' } }],
  outputs: [{ id: 'out', label: 'Out', quantity: 'q', unit: 'u', source: { kind: 'step', ref: 'gap' } }],
  actions: [],
});

test('rejects a document without the collections the editor walks', () => {
  assert.equal(readDocument({ module: {} }), null);
  assert.equal(readDocument(null), null);
});

test('the outline records what each element reads and what reads it', () => {
  const outline = buildOutline(doc());
  const gap = outline.find(n => n.id === 'gap');
  assert.deepEqual(usesOf(outline, gap).map(n => n.id).sort(), ['level', 'limit']);
  assert.deepEqual(usedBy(outline, { kind: 'step', id: 'gap' }).map(n => n.id).sort(), ['low', 'out']);
  assert.deepEqual(usedBy(outline, { kind: 'output', id: 'out' }), []);
});

test('issue paths map to the element they name', () => {
  assert.deepEqual(selectionForIssuePath('steps/gap/operands/0'), { kind: 'step', id: 'gap' });
  assert.deepEqual(selectionForIssuePath('inputs/level'), { kind: 'input', id: 'level' });
  assert.equal(selectionForIssuePath('nonsense'), null);
});

test('adding a step gives a unique id and removal leaves the rest untouched', () => {
  const start = doc();
  const { doc: next, id } = addStep(start);
  assert.notEqual(id, 'gap');
  assert.equal(next.steps.length, 2);
  assert.equal(documentsEqual(removeElement(next, { kind: 'step', id }), start), true);
});
