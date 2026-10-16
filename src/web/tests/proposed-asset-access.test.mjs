/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, panel;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/proposedAssetAccess.ts');
  panel = await vite.ssrLoadModule('/src/views/ProposedAssetAccessPanel.tsx');
});
after(async () => { await vite?.close(); });

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const assetId = '04076374-92c5-4239-9220-d11ca7a5f400';
const permission = 'assets.read';
const signal = () => new AbortController().signal;
const row = { userId, permission, offset: 0, limit: 100,
  assets: [{ id: assetId, name: 'Well 101' }], isDiagnosticOnly: true };

test('proposed resource list only accepts a server-filtered diagnostic page', async () => {
  let count = 0;
  const result = await api.loadProposedAssetAccess(
    userId, permission, 0, signal(), async (url, request) => {
      count++;
      assert.equal(url, '/api/v1/security/users/' + userId +
        '/proposed-assets?permission=assets.read&offset=0&limit=100');
      assert.equal(request.credentials, 'same-origin');
      assert.equal(request.cache, 'no-store');
      return new Response(JSON.stringify(row));
    });
  assert.deepEqual(result, row);
  assert.equal(count, 1);
});

test('reject invalid request and malformed or forged preview results', async () => {
  let writes = 0;
  await assert.rejects(api.loadProposedAssetAccess(
    userId, '???', 0, signal(), async () => { writes++; }),
  { kind: 'invalid' });
  await assert.rejects(api.loadProposedAssetAccess(
    userId, permission, -1, signal(), async () => { writes++; }),
  { kind: 'invalid' });
  assert.equal(writes, 0);

  for (const invalid of [
    { ...row, isDiagnosticOnly: false },
    { ...row, userId: assetId },
    { ...row, permission: 'assets.manage' },
    { ...row, offset: 100 },
    { ...row, limit: 101 },
    { ...row, assets: [{ ...row.assets[0], id: 'INVALID' }] },
    { ...row, assets: [{ ...row.assets[0], name: '' }] },
    { ...row, assets: [row.assets[0], row.assets[0]] },
  ]) {
    await assert.rejects(api.loadProposedAssetAccess(
      userId, permission, 0, signal(),
      async () => new Response(JSON.stringify(invalid))),
    { kind: 'invalidResponse' });
  }
  await assert.rejects(api.loadProposedAssetAccess(
    userId, permission, 0, signal(), async () =>
      new Response('{}', { status: 403 })),
  { kind: 'denied' });
});

test('read-only panel never claims accessible Assets before a server query', () => {
  const html = renderToStaticMarkup(createElement(
    panel.ProposedAssetAccessPanel,
    { userId, permissions: [
      { key: permission, category: 'Assets',
        scopeKind: 'ResourceScoped', isPrivileged: false },
      { key: 'logic.read', category: 'Logic',
        scopeKind: 'DeploymentWide', isPrivileged: false },
    ] }));
  assert.match(html, /Proposed resource access/);
  assert.match(html, /read-only/);
  assert.match(html, /assets.read/);
  assert.doesNotMatch(html, /logic.read/);
  assert.doesNotMatch(html, /Well 101/);
});

test('rejects empty UUID subjects and Assets and non-200 success statuses', async () => {
  const empty = '00000000-0000-0000-0000-000000000000';
  let called = false;
  await assert.rejects(api.loadProposedAssetAccess(empty, 'assets.read', 0, new AbortController().signal,
    async () => { called = true; throw new Error('must not call'); }), { kind: 'invalid' });
  assert.equal(called, false);
  await assert.rejects(api.loadProposedAssetAccess(userId, permission, 0, signal(),
    async () => new Response(JSON.stringify({ ...row, assets: [{ id: empty, name: 'Invalid' }] }))),
    { kind: 'invalidResponse' });
  await assert.rejects(api.loadProposedAssetAccess(userId, permission, 0, signal(),
    async () => new Response(JSON.stringify(row), { status: 202 })), { kind: 'invalidResponse' });
});
