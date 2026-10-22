import test from 'node:test';
import assert from 'node:assert/strict';
import { fetchChildren, fetchSearch } from '../src/views/assetBrowseApi.ts';

const signal = new globalThis.AbortController().signal;

async function requestsDuring(action) {
  const original = globalThis.fetch;
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url: new globalThis.URL(url, 'https://autopilot.test'), options });
    return { ok: true, status: 200, json: async () => [] };
  };
  try {
    await action();
    return requests;
  } finally {
    globalThis.fetch = original;
  }
}

test('a connection scope stays on root, child and paged search requests', async () => {
  const owner = { connectionId: 'connection A' };
  const requests = await requestsDuring(async () => {
    await fetchChildren(null, 0, signal, owner);
    await fetchChildren('parent/one', 100, signal, owner);
    await fetchSearch('ESP & lift', 50, signal, owner);
  });
  for (const { url, options } of requests) {
    assert.equal(url.searchParams.get('ownerConnectionId'), owner.connectionId);
    assert.equal(url.searchParams.has('standalone'), false);
    assert.equal(options.signal, signal);
    assert.equal(options.credentials, 'same-origin');
  }
  assert.equal(requests[1].url.searchParams.get('parentAssetId'), 'parent/one');
  assert.equal(requests[1].url.searchParams.get('offset'), '100');
  assert.equal(requests[2].url.searchParams.get('q'), 'ESP & lift');
  assert.equal(requests[2].url.searchParams.get('offset'), '50');
});

test('standalone is an explicit scope at every hierarchy level and in search', async () => {
  const requests = await requestsDuring(async () => {
    await fetchChildren(null, 0, signal, { connectionId: null });
    await fetchChildren('parent', 0, signal, { connectionId: null });
    await fetchSearch('well', 0, signal, { connectionId: null });
  });
  for (const { url } of requests) {
    assert.equal(url.searchParams.get('standalone'), 'true');
    assert.equal(url.searchParams.has('ownerConnectionId'), false);
  }
});

test('existing unscoped browse and search callers keep their behavior', async () => {
  const requests = await requestsDuring(async () => {
    await fetchChildren(null, 0, signal);
    await fetchChildren('parent', 0, signal);
    await fetchSearch('well', 0, signal);
  });
  for (const { url } of requests) {
    assert.equal(url.searchParams.has('standalone'), false);
    assert.equal(url.searchParams.has('ownerConnectionId'), false);
  }
});
