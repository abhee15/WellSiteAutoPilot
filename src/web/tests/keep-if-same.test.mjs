import test from 'node:test';
import assert from 'node:assert/strict';
import { keepIfSame } from '../src/keepIfSame.ts';

test('keeps the current copy when the new one holds the same data', () => {
  const current = [{ id: '1', name: 'Pad' }, { id: '2', name: 'Well', parentAssetId: '1' }];
  const next = JSON.parse(JSON.stringify(current));
  assert.equal(keepIfSame(current, next), current);
});

test('takes the new copy when anything differs', () => {
  const current = [{ id: '1', name: 'Pad' }];
  const renamed = [{ id: '1', name: 'Pad 7' }];
  assert.equal(keepIfSame(current, renamed), renamed);
  const added = [{ id: '1', name: 'Pad' }, { id: '2', name: 'Well' }];
  assert.equal(keepIfSame(current, added), added);
  const flipped = [{ id: '1', name: 'Pad', isActive: false }];
  assert.equal(keepIfSame([{ id: '1', name: 'Pad', isActive: true }], flipped), flipped);
});

test('a different order or an empty list is a change, never a false match', () => {
  const first = [{ id: '1' }, { id: '2' }];
  const swapped = [{ id: '2' }, { id: '1' }];
  assert.equal(keepIfSame(first, swapped), swapped);
  const empty = [];
  assert.equal(keepIfSame(first, empty), empty);
});

test('data that cannot be compared is replaced, not trusted', () => {
  const loop = {}; loop.self = loop;
  const fresh = { ok: true };
  assert.equal(keepIfSame(loop, fresh), fresh);
});
