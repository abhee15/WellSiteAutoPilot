import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, inventory, controls;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  inventory = await vite.ssrLoadModule('/src/views/assetInventory.ts');
  controls = await vite.ssrLoadModule('/src/views/AssetInventoryControls.tsx');
});
after(async () => { await vite?.close(); });

const types = new Map([['type-well', 'Production Well'], ['type-tank', 'Storage Tank']]);
const assets = [
  { id: 'z', name: 'Zeta', assetTypeId: 'type-well', parentAssetId: null, isActive: true,
    createdAtUtc: '2026-10-13T10:00:00Z' },
  { id: 'a', name: 'Alpha', assetTypeId: 'type-tank', parentAssetId: 'z', isActive: false,
    createdAtUtc: '2026-10-14T10:00:00Z' },
  { id: 'b', name: 'Alpha', assetTypeId: 'type-well', parentAssetId: null, isActive: true,
    createdAtUtc: '2026-10-12T10:00:00Z' },
];
const criteria = { search: '', activity: 'all', hierarchy: 'all', sort: 'name' };
const ids = value => value.map(asset => asset.id);

test('search matches only supplied authorized records and readable type names', () => {
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, search: 'STORAGE tank' })), ['a']);
  assert.deepEqual(inventory.filterAssetInventory(assets, types,
    { ...criteria, search: 'unknown' }), []);
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, search: ' z ' })), ['z']);
  assert.equal(assets.length, 3);
});
test('status and hierarchy filters compose safely', () => {
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, activity: 'active', hierarchy: 'roots' })), ['b', 'z']);
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, activity: 'inactive', hierarchy: 'children' })), ['a']);
  assert.deepEqual(inventory.filterAssetInventory(assets, types,
    { ...criteria, activity: 'inactive', hierarchy: 'roots' }), []);
});
test('name ties and chronological ordering are deterministic and non-mutating', () => {
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types, criteria)), ['a', 'b', 'z']);
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, sort: 'newest' })), ['a', 'z', 'b']);
  assert.deepEqual(ids(inventory.filterAssetInventory(assets, types,
    { ...criteria, sort: 'oldest' })), ['b', 'z', 'a']);
  assert.equal(assets[0].id, 'z');
});
test('responsive control markup exposes accessible labels and counts', () => {
  const markup = renderToStaticMarkup(createElement(controls.AssetInventoryControls, {
    criteria, onChange: () => {}, matchingCount: 2, totalCount: 3,
  }));
  assert.match(markup, /Asset inventory controls/);
  assert.match(markup, /Root assets/);
  assert.match(markup, /role="group"/);
  assert.match(markup, /Showing 2 of 3 loaded assets/);
});
