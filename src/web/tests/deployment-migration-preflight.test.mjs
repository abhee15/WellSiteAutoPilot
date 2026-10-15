/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, panel;
before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent',
  });
  api = await vite.ssrLoadModule('/src/views/deploymentMigrationPreflight.ts');
  panel = await vite.ssrLoadModule('/src/views/DeploymentMigrationPreflightPanel.tsx');
});
after(async () => { await vite?.close(); });

const a = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const b = '04076374-92c5-4239-9220-d11ca7a5f400';
const evidence = {
  activeUserCount: 2, activeAssetCount: 3,
  matchingUserCount: 1, expansionCount: 1, reductionCount: 2,
  users: [
    { userId: a, expectedLegacyGrantCount: 2, recordedGrantCount: 1,
      expansionCount: 1, reductionCount: 2, recordedGrantsMatchLegacy: false },
    { userId: b, expectedLegacyGrantCount: 0, recordedGrantCount: 0,
      expansionCount: 0, reductionCount: 0, recordedGrantsMatchLegacy: true },
  ],
  allActiveUsersMatchRecordedGrants: false,
  isDiagnosticOnly: true,
};
const signal = () => new AbortController().signal;

test('deployment parity retrieves one authenticated, uncached read-only snapshot', async () => {
  let count = 0;
  const result = await api.loadDeploymentParity(signal(), async (url, init) => {
    count++;
    assert.equal(url, '/api/v1/security/migration-preflight');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.method, undefined);
    return new Response(JSON.stringify(evidence));
  });
  assert.deepEqual(result, evidence);
  assert.equal(count, 1);
});

test('deployment report rejects forged parity and truncated/inconsistent totals', async () => {
  const invalid = [
    { ...evidence, isDiagnosticOnly: false },
    { ...evidence, activeUserCount: 1 },
    { ...evidence, users: [{ ...evidence.users[0], userId: '00000000-0000-0000-0000-000000000000' }, evidence.users[1]] },
    { ...evidence, users: [{ ...evidence.users[0], userId: 'b86c36a2-3349-4020-a3ce7ff42991' }, evidence.users[1]] },
    { ...evidence, activeAssetCount: 2001 },
    { ...evidence, matchingUserCount: 2 },
    { ...evidence, expansionCount: 0 },
    { ...evidence, reductionCount: 1 },
    { ...evidence, allActiveUsersMatchRecordedGrants: true },
    { ...evidence, users: [evidence.users[0], evidence.users[0]] },
    { ...evidence, users: [
      { ...evidence.users[0], recordedGrantsMatchLegacy: true }, evidence.users[1]] },
    { ...evidence, users: [
      { ...evidence.users[0], userId: 'bad-id' }, evidence.users[1]] },
  ];
  for (const bad of invalid) {
    await assert.rejects(api.loadDeploymentParity(signal(),
      async () => new Response(JSON.stringify(bad))),
    { kind: 'invalidResponse' });
  }
  await assert.rejects(api.loadDeploymentParity(signal(),
    async () => new Response('{}', { status: 403 })),
  { kind: 'denied' });
});

test('Admin view starts with no unverified snapshot claims or cutover action', () => {
  const html = renderToStaticMarkup(createElement(
    panel.DeploymentMigrationPreflightPanel, { users: [] }));
  assert.match(html, /Deployment migration preflight/);
  assert.match(html, /read-only diagnostic/);
  assert.doesNotMatch(html, /All currently active users match/);
  assert.doesNotMatch(html, /Enable migration/);
});
