import test from 'node:test';
import assert from 'node:assert/strict';
import { buildAssetTree, ancestorIds } from '../src/views/assetHierarchy.ts';

const a = (id, name, parentAssetId) => ({ id, name, parentAssetId });
const names = nodes => nodes.map(n => [n.asset.name, names(n.children)]);

test('builds a sorted hierarchy from parent ids', () => {
  const tree = buildAssetTree([a('3', 'Well B', '2'), a('1', 'Field'), a('2', 'Pad', '1'), a('4', 'Well A', '2')]);
  assert.deepEqual(names(tree), [['Field', [['Pad', [['Well A', []], ['Well B', []]]]]]]);
});

test('assets with hidden parents are shown at the top level', () => {
  const tree = buildAssetTree([a('3', 'Well', '2'), a('1', 'Field')]);
  assert.deepEqual(tree.map(n => n.asset.name), ['Field', 'Well']);
});

test('cycles and self-parents never loop', () => {
  const tree = buildAssetTree([a('1', 'X', '2'), a('2', 'Y', '1'), a('3', 'Z', '3')]);
  const count = nodes => nodes.reduce((n, x) => n + 1 + count(x.children), 0);
  assert.equal(count(tree), 3);
});

test('ancestors of a selection are forced open', () => {
  const assets = [a('1', 'F'), a('2', 'P', '1'), a('3', 'W', '2')];
  assert.deepEqual([...ancestorIds(assets, '3')].sort(), ['1', '2']);
  assert.equal(ancestorIds(assets, null).size, 0);
});

import { groupRootsByOwner } from '../src/views/assetHierarchy.ts';

const owned = (id, name, ownerConnectionId) => ({ id, name, ownerConnectionId });
const owner = (connectionId, name) => ({ connectionId, name, isEnabled: true, topLevelCount: 1, assetCount: 1 });

test('groups top-level assets under their Connection, Standalone last', () => {
  const tree = buildAssetTree([owned('1', 'Zed', null), owned('2', 'Field', 'c2'), owned('3', 'Other', 'c1')]);
  const groups = groupRootsByOwner(tree, [owner('c2', 'Beta'), owner('c1', 'Alpha'), owner(null, null)]);
  assert.deepEqual(groups.map(g => [g.owner.name, g.roots.map(r => r.asset.name)]),
    [['Alpha', ['Other']], ['Beta', ['Field']], [null, ['Zed']]]);
});

test('stays flat when no Connection owns anything', () => {
  const tree = buildAssetTree([owned('1', 'Zed', null)]);
  assert.equal(groupRootsByOwner(tree, [owner(null, null)]), null);
  assert.equal(groupRootsByOwner(tree, []), null);
});

test('an owner the server did not list is still shown', () => {
  const tree = buildAssetTree([owned('1', 'Field', 'gone'), owned('2', 'Mine', 'c1')]);
  const groups = groupRootsByOwner(tree, [owner('c1', 'Alpha')]);
  assert.deepEqual(groups.map(g => g.owner.connectionId).sort(), ['c1', 'gone']);
});
