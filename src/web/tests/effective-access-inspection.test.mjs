/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, component;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/effectiveAccessInspection.ts');
  component = await vite.ssrLoadModule('/src/views/EffectiveAccessInspector.tsx');
});
after(async () => { await vite?.close(); });

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const well = '04076374-92c5-4239-9220-d11ca7a5f400';
const grantId = 'e4c14a68-aa15-4d6a-a9f8-e7f94d710f23';
const signal = () => new AbortController().signal;
const row = {
  userId, assetId: well, assetName: 'Well 101',
  permissions: [
    { permission: 'assets.read', scopeKind: 'ResourceScoped',
      legacyAllowed: true, proposedAllowed: true,
      proposedReasonCode: 'SECURITY_PERMISSION_GRANTED',
      proposedGrantId: grantId },
    { permission: 'assets.manage', scopeKind: 'ResourceScoped',
      legacyAllowed: false, proposedAllowed: true,
      proposedReasonCode: 'SECURITY_PERMISSION_GRANTED',
      proposedGrantId: grantId },
    { permission: 'recommendations.decide', scopeKind: 'ResourceScoped',
      legacyAllowed: true, proposedAllowed: false,
      proposedReasonCode: 'SECURITY_SCOPE_DENIED',
      proposedGrantId: null },
  ],
  wideningCount: 1, reductionCount: 1, isDiagnosticOnly: true,
};

test('Effective access query is a single authenticated uncached GET', async () => {
  let count = 0;
  const result = await api.loadEffectiveAccess(userId, well, signal(),
    async (url, init) => {
      count++;
      assert.equal(url, '/api/v1/security/users/' + userId +
        '/effective-access?assetId=' + well);
      assert.equal(init.credentials, 'same-origin');
      assert.equal(init.cache, 'no-store');
      return new Response(JSON.stringify(row));
    });
  assert.deepEqual(result, row);
  assert.equal(count, 1);
});

test('Deployment-wide inspection cannot masquerade as per-Asset parity', async () => {
  const global = {
    ...row, assetId: null, assetName: null,
    wideningCount: 0, reductionCount: 0,
    permissions: [{ permission: 'logic.read',
      scopeKind: 'DeploymentWide', legacyAllowed: true,
      proposedAllowed: true, proposedReasonCode: 'SECURITY_PERMISSION_GRANTED',
      proposedGrantId: grantId }],
  };
  const result = await api.loadEffectiveAccess(userId, null, signal(),
    async (url) => {
      assert.equal(url, '/api/v1/security/users/' + userId + '/effective-access');
      return new Response(JSON.stringify(global));
    });
  assert.deepEqual(result, global);
  await assert.rejects(api.loadEffectiveAccess(userId, null, signal(),
    async () => new Response(JSON.stringify(row))),
  { kind: 'invalidResponse' });
});

test('Client rejects mismatched, forged or miscounted comparisons', async () => {
  for (const invalid of [
    { ...row, isDiagnosticOnly: false },
    { ...row, userId: grantId },
    { ...row, assetId: grantId },
    { ...row, wideningCount: 0 },
    { ...row, reductionCount: 0 },
    { ...row, permissions: [...row.permissions, row.permissions[0]] },
    { ...row, permissions: [] },
    { ...row, permissions: [{ ...row.permissions[0],
      proposedGrantId: null }] },
    { ...row, permissions: [{ ...row.permissions[2],
      proposedGrantId: grantId }] },
  ]) {
    await assert.rejects(api.loadEffectiveAccess(userId, well, signal(),
      async () => new Response(JSON.stringify(invalid))),
    { kind: 'invalidResponse' });
  }
});

test('Access inspector initializes without claiming any authorization state', () => {
  const html = renderToStaticMarkup(createElement(
    component.EffectiveAccessInspector, { userId, assets: [
      { id: well, name: 'Well 101', isActive: true },
    ] }));
  assert.match(html, /Inspect effective access/);
  assert.match(html, /read-only diagnostic/);
  assert.doesNotMatch(html, /Expanded/);
});
