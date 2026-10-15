/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, rollback, panel;
before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent',
  });
  rollback = await vite.ssrLoadModule('/src/views/rollbackLegacyGrants.ts');
  panel = await vite.ssrLoadModule('/src/views/DeploymentMigrationPreflightPanel.tsx');
});
after(async () => { await vite?.close(); });

const signal = () => new AbortController().signal;
const receipt = {
  migratedGrantsInspected: 9,
  grantsDeactivated: 8,
  liveAuthorizationUnchanged: true,
};

test('rollback requires separate explicit confirmation and makes one POST', async () => {
  let calls = 0;
  await assert.rejects(rollback.rollbackLegacyGrants(
    false, signal(), async () => { calls++; }),
  { kind: 'invalid' });
  const result = await rollback.rollbackLegacyGrants(
    true, signal(), async (url, request) => {
      calls++;
      assert.equal(url, '/api/v1/security/rollback-legacy-grants');
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

test('rollback refuses unauthenticated, conflicted, and forged responses', async () => {
  for (const status of [401, 403, 409]) {
    await assert.rejects(rollback.rollbackLegacyGrants(
      true, signal(), async () => new Response('{}', { status })),
      { kind: status === 409 ? 'conflict' : 'denied' });
  }
  for (const value of [
    { ...receipt, liveAuthorizationUnchanged: false },
    { ...receipt, migratedGrantsInspected: 601 },
    { ...receipt, grantsDeactivated: 601 },
    { ...receipt, grantsDeactivated: 10 },
    { ...receipt, grantsDeactivated: -1 },
  ]) {
    await assert.rejects(rollback.rollbackLegacyGrants(
      true, signal(), async () => new Response(JSON.stringify(value))),
      { kind: 'invalidResponse' });
  }
});

test('uncertain rollback completion never automatically retries', async () => {
  let calls = 0;
  await assert.rejects(rollback.rollbackLegacyGrants(
    true, signal(), async () => {
      calls++;
      throw new Error('network disappeared after server commit');
    }), { kind: 'unavailable' });
  assert.equal(calls, 1);
});

test('migration readiness page never offers unconfirmed rollback by default', () => {
  const html = renderToStaticMarkup(createElement(
    panel.DeploymentMigrationPreflightPanel, { users: [] }));
  assert.match(html, /Deployment migration readiness/);
  assert.doesNotMatch(html, /ROLLBACK MIGRATED GRANTS/);
  assert.doesNotMatch(html, /Rollback migrated grants/);
});
