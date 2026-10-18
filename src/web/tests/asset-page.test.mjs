import test from 'node:test';
import assert from 'node:assert/strict';
import { assetAncestors, assetChildren, logicOnAsset, fetchAssetRuns } from '../src/views/assetPage.ts';

const a = (id, name, parentAssetId) => ({ id, name, parentAssetId });

test('ancestors run top-down and stop at hidden parents or cycles', () => {
  const assets = [a('1', 'Field'), a('2', 'Pad', '1'), a('3', 'Well', '2')];
  assert.deepEqual(assetAncestors(assets, '3').map(x => x.name), ['Field', 'Pad']);
  assert.deepEqual(assetAncestors([a('3', 'Well', '2')], '3'), []);
  assert.deepEqual(assetAncestors([a('1', 'X', '2'), a('2', 'Y', '1')], '1').map(x => x.id), ['2']);
});

test('children are sorted by name', () => {
  const assets = [a('1', 'P'), a('3', 'B', '1'), a('2', 'A', '1'), a('4', 'Z', '9')];
  assert.deepEqual(assetChildren(assets, '1').map(x => x.name), ['A', 'B']);
});

test('logic on an asset comes from saved revision bindings', () => {
  const cfg = [
    { id: 'c1', name: 'B', revisions: [{ assetBindings: [{ assetId: '3' }] }] },
    { id: 'c2', name: 'A', revisions: [{ assetBindings: [{ assetId: '3' }, { assetId: '4' }] }] },
    { id: 'c3', name: 'C', revisions: [{ assetBindings: [{ assetId: '4' }] }] },
    { id: 'c4', name: 'D' },
  ];
  assert.deepEqual(logicOnAsset(cfg, '3').map(x => x.id), ['c2', 'c1']);
});

test('asset runs ask the server to filter and cap the list', async () => {
  let url = '';
  await fetchAssetRuns('a b', {}, async u => {
    url = u; return { ok: true, json: async () => [] };
  });
  assert.equal(url, '/api/v1/executions?assetId=a%20b&limit=10');
});
