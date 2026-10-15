/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, api;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/grantAssetDirectory.ts');
});
after(async () => { await vite?.close(); });
const signal = () => new AbortController().signal;
const asset = i => ({ id: '00000000-0000-4000-8000-' +
  String(i).padStart(12, '0'), name: 'Well ' + i, isActive: true });

test('Admin directory reads all pages, not just the first 100 wells', async () => {
  const expected = Array.from({ length: 202 }, (_, i) => asset(i));
  const visited = [];
  const records = await api.loadGrantAssetDirectory(signal(), async (url, opts) => {
    visited.push(url);
    assert.equal(opts.credentials, 'same-origin');
    assert.equal(opts.cache, 'no-store');
    const offset = Number(new URL(url, 'https://local.invalid').searchParams.get('offset'));
    return new Response(JSON.stringify(expected.slice(offset, offset + 200)));
  });
  assert.deepEqual(records, expected);
  assert.deepEqual(visited, [
    '/api/v1/security/assets/directory?offset=0&limit=200',
    '/api/v1/security/assets/directory?offset=200&limit=200',
  ]);
});

test('Missing, duplicate or malformed directory pages fail closed', async () => {
  for (const invalid of [
    [{ ...asset(1), isActive: 'yes' }],
    [{ ...asset(1), id: 'not-a-guid' }],
    [{ ...asset(1), name: '' }],
    [...Array.from({ length: 201 }, (_, i) => asset(i))],
  ]) {
    await assert.rejects(api.loadGrantAssetDirectory(signal(),
      async () => new Response(JSON.stringify(invalid))),
    { kind: 'invalid' });
  }

  let page = 0;
  const duplicate = Array.from({ length: 200 }, (_, i) => asset(i));
  await assert.rejects(api.loadGrantAssetDirectory(signal(), async () => {
    page++;
    return new Response(JSON.stringify(page === 1 ? duplicate : [asset(0)]));
  }), { kind: 'invalid' });
  await assert.rejects(api.loadGrantAssetDirectory(signal(),
    async () => new Response('Unauthorized', { status: 403 })),
  { kind: 'unavailable' });
  await assert.rejects(api.loadGrantAssetDirectory(signal(),
    async () => { throw new Error('Directory unavailable'); }),
  { kind: 'unavailable' });
});

test('Directory cannot silently truncate after verification cap', async () => {
  let reads = 0;
  await assert.rejects(api.loadGrantAssetDirectory(signal(), async () => {
    const offset = reads++ * 200;
    return new Response(JSON.stringify(
      Array.from({ length: 200 }, (_, i) => asset(offset + i))));
  }), { kind: 'tooLarge' });
  assert.equal(reads, 101);
});
