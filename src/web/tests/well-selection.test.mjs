import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';
let vite, wellsUnder;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ wellsUnder } = await vite.ssrLoadModule('/src/views/wellSelection.ts'));
});
after(async () => { await vite?.close(); });

const owner = 'connection-a';
const node = (id, type = 'well', childCount = 0) => ({ id, name: id, assetTypeId: type,
  ownerConnectionId: owner, parentAssetId: null, isActive: true, childCount });
const signal = new globalThis.AbortController().signal;
async function fixture(action, answer) {
  const original = globalThis.fetch;
  globalThis.fetch = async url => ({ ok: true, status: 200, json: async () => answer(new globalThis.URL(url, 'https://test.invalid')) });
  try { await action(); } finally { globalThis.fetch = original; }
}
test('branch selection reaches all 10,000 wells beyond loaded pages and excludes grouping nodes', async () => {
  const calls = [];
  await fixture(async () => {
    const result = await wellsUnder(owner, null, new Set(['well']), signal);
    assert.equal(result.length, 10_000);
    assert.equal(new Set(result.map(item => item.id)).size, 10_000);
    assert.equal(result.at(-1).id, 'well-9999');
    assert.ok(calls.every(url => url.searchParams.get('ownerConnectionId') === owner));
    assert.ok(calls.some(url => url.searchParams.get('offset') === '9900'));
  }, url => {
    calls.push(url);
    if (!url.searchParams.has('parentAssetId')) return [node('field', 'group', 10_000)];
    const offset = Number(url.searchParams.get('offset'));
    return Array.from({ length: Math.min(100, 10_000 - offset) }, (_, index) => node('well-' + (offset + index)));
  });
});
test('a later-page failure rejects the operation rather than returning a partial selection', async () => {
  await fixture(async () => {
    await assert.rejects(wellsUnder(owner, 'field', new Set(['well']), signal));
  }, url => {
    if (Number(url.searchParams.get('offset')) >= 100) throw new Error('later page failed');
    return Array.from({ length: 100 }, (_, index) => node('well-' + index));
  });
});
test('a foreign owner is refused and canceled selection returns no partial result', async () => {
  await fixture(async () => {
    await assert.rejects(wellsUnder(owner, null, new Set(['well']), signal));
  }, () => [{ ...node('foreign'), ownerConnectionId: 'connection-b' }]);
  const canceled = new globalThis.AbortController(); canceled.abort();
  await fixture(async () => {
    await assert.rejects(wellsUnder(owner, null, new Set(['well']), canceled.signal));
  }, () => [node('well')]);
});
