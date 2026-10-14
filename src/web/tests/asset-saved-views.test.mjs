import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
let vite, saved;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  saved = await vite.ssrLoadModule('/src/views/assetSavedViews.ts');
});
after(async () => { await vite?.close(); });
const criteria = { search: 'pump', activity: 'active', hierarchy: 'roots', sort: 'name' };
const view = { id: 'one', name: 'Pump wells', criteria };
function storage() {
  const data = new Map();
  return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value),
    snapshot: () => [...data.entries()] };
}
test('asset preferences are stored by signed-in user and never contain asset records', () => {
  const memory = storage();
  assert.equal(saved.persistSavedAssetViews('operator-one', [view], memory), true);
  assert.deepEqual(saved.loadSavedAssetViews('operator-one', memory), [view]);
  assert.deepEqual(saved.loadSavedAssetViews('operator-two', memory), []);
  assert.equal(memory.snapshot().length, 1);
  assert.equal(memory.snapshot()[0][1].includes('assetExternalId'), false);
});
test('invalid storage, duplicates and oversized content are rejected safely', () => {
  assert.deepEqual(saved.parseSavedAssetViews('{not-json'), []);
  assert.deepEqual(saved.parseSavedAssetViews('x'.repeat(50_001)), []);
  assert.deepEqual(saved.parseSavedAssetViews(JSON.stringify({ version: 2, views: [view] })), []);
  assert.deepEqual(saved.parseSavedAssetViews(JSON.stringify({
    version: 1, views: [view, { ...view, id: 'two', name: 'PUMP WELLS' },
      { ...view, id: 'three', criteria: { ...criteria, hierarchy: 'other' } }],
  })), [view]);
});
test('browser storage errors and missing identities do not produce false save success', () => {
  const broken = { getItem() { throw new Error('disabled'); },
    setItem() { throw new Error('quota'); } };
  assert.deepEqual(saved.loadSavedAssetViews('operator', broken), []);
  assert.equal(saved.persistSavedAssetViews('operator', [view], broken), false);
  assert.equal(saved.persistSavedAssetViews(null, [view], storage()), false);
  assert.equal(saved.persistSavedAssetViews('operator',
    Array.from({ length: 21 }, (_, i) => ({ ...view, id: String(i) })), storage()), false);
});
