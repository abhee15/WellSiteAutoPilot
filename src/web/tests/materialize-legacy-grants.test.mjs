/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, migration;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  migration = await vite.ssrLoadModule('/src/views/materializeLegacyGrants.ts');
});
after(async () => { await vite?.close(); });

const signal = () => new AbortController().signal;
const receipt = {
  activeUsersInspected: 3,
  activeAssetsInspected: 2,
  grantsCreated: 6,
  usersAlreadyMatching: 0,
  recordedGrantsMatchLegacy: true,
  liveAuthorizationUnchanged: true,
};

test('migration requires affirmative confirmation and issues exactly one POST', async () => {
  let calls = 0;
  await assert.rejects(migration.materializeLegacyGrants(
    false, signal(), async () => { calls++; }),
  { kind: 'invalid' });
  assert.equal(calls, 0);
  const result = await migration.materializeLegacyGrants(
    true, signal(), async (url, request) => {
      calls++;
      assert.equal(url, '/api/v1/security/materialize-legacy-grants');
      assert.equal(request.method, 'POST');
      assert.equal(request.credentials, 'same-origin');
      assert.equal(request.cache, 'no-store');
      assert.equal(request.headers['Content-Type'], 'application/json');
      assert.deepEqual(JSON.parse(request.body), { confirmed: true });
      return new Response(JSON.stringify(receipt));
    });
  assert.equal(calls, 1);
  assert.deepEqual(result, receipt);
});

test('conflicts and malformed responses do not silently signal success', async () => {
  await assert.rejects(migration.materializeLegacyGrants(
    true, signal(), async () => new Response('{}', { status: 409 })),
  { kind: 'conflict' });
  for (const bad of [
    { ...receipt, liveAuthorizationUnchanged: false },
    { ...receipt, recordedGrantsMatchLegacy: false },
    { ...receipt, grantsCreated: -1 },
    { ...receipt, grantsCreated: 601 },
    { ...receipt, activeUsersInspected: 101 },
    { ...receipt, usersAlreadyMatching: 4 },
    { ...receipt, activeAssetsInspected: 2001 },
  ]) {
    await assert.rejects(migration.materializeLegacyGrants(
      true, signal(), async () => new Response(JSON.stringify(bad))),
    { kind: 'invalidResponse' });
  }
});

test('uncertain network outcome is an explicit failure, never a retry', async () => {
  let calls = 0;
  await assert.rejects(migration.materializeLegacyGrants(
    true, signal(), async () => {
      calls++;
      throw new Error('network dropped after commit');
    }), { kind: 'unavailable' });
  assert.equal(calls, 1);
});
