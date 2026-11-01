import test from 'node:test';
import assert from 'node:assert/strict';
import { assetAncestors, assetChildren } from '../src/views/assetPage.ts';

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
