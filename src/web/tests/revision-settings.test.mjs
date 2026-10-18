import test from 'node:test';
import assert from 'node:assert/strict';
import { before, after } from 'node:test';
import { createServer } from 'vite';

let vite, describeSettings;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ describeSettings } = await vite.ssrLoadModule('/src/views/revisionSettings.ts'));
});
after(async () => { await vite?.close(); });

const manifest = JSON.stringify({ parameterSchema: { type: 'object', properties: {
  targetFillage: { type: 'number', title: 'Target fillage (%)' }, mode: { type: 'string', enum: ['Hold', 'Trim'] } } } });

test('uses the schema labels and shows each saved value', () => {
  assert.deepEqual(describeSettings('{"targetFillage":75,"mode":"Trim"}', manifest), [
    { label: 'Target fillage (%)', value: '75' }, { label: 'Mode', value: 'Trim' }]);
});

test('falls back to raw names without a schema and renders other values as JSON', () => {
  assert.deepEqual(describeSettings('{"limit":5,"on":true,"list":[1,2]}', '{}'), [
    { label: 'limit', value: '5' }, { label: 'on', value: 'Yes' }, { label: 'list', value: '[1,2]' }]);
});

test('shows nothing for empty, malformed or non-object settings', () => {
  assert.equal(describeSettings('{}', manifest), null);
  assert.equal(describeSettings('nope', manifest), null);
  assert.equal(describeSettings('[]', manifest), null);
  assert.equal(describeSettings(undefined, undefined), null);
});
