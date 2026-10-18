import test from 'node:test';
import assert from 'node:assert/strict';
import { attributeFields, buildAttributes } from '../src/views/assetAttributeForm.ts';

const schema = JSON.stringify({ type: 'object', required: ['api_number', 'depthFt'], properties: {
  api_number: { type: 'string', maxLength: 5, description: 'Well API number' },
  depthFt: { type: 'integer', minimum: 0, maximum: 30000, title: 'Depth (ft)' },
  gasLift: { type: 'boolean' },
  lift: { type: 'string', enum: ['ESP', 'Rod'] },
} });

test('simple flat schemas become labelled typed fields', () => {
  const fields = attributeFields(schema);
  assert.deepEqual(fields.map(f => [f.key, f.label, f.kind, f.required]), [
    ['api_number', 'Api number', 'text', true], ['depthFt', 'Depth (ft)', 'integer', true],
    ['gasLift', 'Gas Lift', 'boolean', false], ['lift', 'Lift', 'choice', false]]);
});

test('nested, empty or malformed schemas fall back to the JSON editor', () => {
  assert.equal(attributeFields('{}'), null);
  assert.equal(attributeFields('not json'), null);
  assert.equal(attributeFields('{"type":"object","properties":{}}'), null);
  assert.equal(attributeFields('{"type":"object","properties":{"a":{"type":"object"}}}'), null);
  assert.equal(attributeFields('{"type":"object","properties":{"a":{"type":"array"}}}'), null);
});

test('values are typed, empty optional fields are omitted, problems are named', () => {
  const fields = attributeFields(schema);
  const ok = buildAttributes(fields, { api_number: ' 42 ', depthFt: '1200', gasLift: true, lift: '' });
  assert.deepEqual(ok.errors, {});
  assert.deepEqual(JSON.parse(ok.json), { api_number: '42', depthFt: 1200, gasLift: true });
  const bad = buildAttributes(fields, { api_number: '123456', depthFt: '1.5' });
  assert.deepEqual(bad.errors, { api_number: 'length', depthFt: 'integer' });
  assert.deepEqual(buildAttributes(fields, {}).errors, { api_number: 'required', depthFt: 'required' });
  assert.equal(buildAttributes(fields, { api_number: 'a', depthFt: '-1' }).errors.depthFt, 'minimum');
  assert.equal(buildAttributes(fields, { api_number: 'a', depthFt: '99999' }).errors.depthFt, 'maximum');
  assert.equal(buildAttributes(fields, { api_number: 'a', depthFt: 'x' }).errors.depthFt, 'number');
});
