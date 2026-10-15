/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, view;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/legacyMigrationPreflight.ts');
  view = await vite.ssrLoadModule('/src/views/LegacyMigrationPreflightPanel.tsx');
});
after(async () => { await vite?.close(); });

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const assetId = '04076374-92c5-4239-9220-d11ca7a5f400';
const signal = () => new AbortController().signal;
const valid = {
  userId,
  activeAssetCount: 2,
  expectedLegacyGrantCount: 2,
  recordedGrantCount: 1,
  expansionCount: 0,
  reductionCount: 2,
  differenceSamples: [
    { assetId: null, permission: 'logic.read',
      legacyAllowed: true, recordedGrantAllowed: false },
    { assetId, permission: 'recommendations.read',
      legacyAllowed: true, recordedGrantAllowed: false },
  ],
  recordedGrantsMatchLegacy: false,
  isDiagnosticOnly: true,
};

test('migration preflight GET is authenticated, uncached and read-only', async () => {
  let requests = 0;
  const result = await api.loadLegacyMigrationPreflight(userId, signal(),
    async (url, request) => {
      requests++;
      assert.equal(url, '/api/v1/security/users/' + userId +
        '/migration-preflight');
      assert.equal(request.credentials, 'same-origin');
      assert.equal(request.cache, 'no-store');
      assert.equal(request.method, undefined);
      return new Response(JSON.stringify(valid));
    });
  assert.deepEqual(result, valid);
  assert.equal(requests, 1);
});

test('reject forged, incomplete or contradictory parity evidence', async () => {
  for (const broken of [
    { ...valid, userId: assetId },
    { ...valid, isDiagnosticOnly: false },
    { ...valid, activeAssetCount: 2001 },
    { ...valid, expansionCount: -1 },
    { ...valid, reductionCount: 1 },
    { ...valid, recordedGrantsMatchLegacy: true },
    { ...valid, differenceSamples: [valid.differenceSamples[0],
      valid.differenceSamples[0]] },
    { ...valid, differenceSamples: [{ ...valid.differenceSamples[0],
      legacyAllowed: false }] },
    { ...valid, differenceSamples: [{ ...valid.differenceSamples[0],
      assetId: 'not-a-guid' }] },
    { ...valid, differenceSamples: new Array(21).fill(valid.differenceSamples[0]) },
  ]) {
    await assert.rejects(api.loadLegacyMigrationPreflight(userId,
      signal(), async () => new Response(JSON.stringify(broken))),
    { kind: 'invalidResponse' });
  }
  await assert.rejects(api.loadLegacyMigrationPreflight(userId,
    signal(), async () => new Response('{}', { status: 403 })),
  { kind: 'denied' });
});

test('panel shows no parity conclusion before server evidence', () => {
  const html = renderToStaticMarkup(createElement(
    view.LegacyMigrationPreflightPanel, {
      userId, assets: [{ id: assetId, name: 'Well 101', isActive: true }],
    }));
  assert.match(html, /Legacy migration preflight/);
  assert.match(html, /does not migrate, approve, or enable/);
  assert.doesNotMatch(html, /matched legacy access in the inspected snapshot/);
});
