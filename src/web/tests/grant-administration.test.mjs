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
  api = await vite.ssrLoadModule('/src/views/grantAdministration.ts');
  component = await vite.ssrLoadModule('/src/views/GrantAdministrationPage.tsx');
});
after(async () => { await vite?.close(); });

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const roleId = '139e5b2f-dd2d-4630-b920-141b7bfb7a62';
const grantId = 'e4c14a68-aa15-4d6a-a9f8-e7f94d710f23';
const wellA = '04076374-92c5-4239-9220-d11ca7a5f400';
const draft = { subjectKind: 'User', userId, roleId,
  scopeKind: 'ExactAssets', assetIds: [wellA] };
const row = { grantId, subjectKind: 'User', userId, groupSid: null,
  roleId, scopeKind: 'ExactAssets', assetIds: [wellA], isActive: true, revision: 1 };
const signal = () => new AbortController().signal;

test('Admin grant listing is uncached and rejects wrong-user or malformed grants', async () => {
  const rows = await api.loadGrants(userId, signal(), async (url, init) => {
    assert.equal(url, '/api/v1/security/users/' + userId + '/grants');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return new Response(JSON.stringify([row]));
  });
  assert.deepEqual(rows, [row]);
  for (const invalid of [
    [{ ...row, userId: roleId }],
    [{ ...row, scopeKind: 'Invalid' }],
    [{ ...row, roleId: 'garbled' }],
    [row, row],
    [{ ...row, revision: 0 }],
  ]) {
    await assert.rejects(api.loadGrants(userId, signal(),
      async () => new Response(JSON.stringify(invalid))),
    { kind: 'invalidResponse' });
  }
});

test('scoped write requires explicit final acknowledgement and one confirmed POST', async () => {
  let count = 0;
  const fetcher = async (url, init) => {
    count++;
    assert.equal(url, '/api/v1/security/users/' + userId + '/grants');
    assert.equal(init.method, 'POST');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.deepEqual(JSON.parse(init.body), { roleId,
      scopeKind: 'ExactAssets', assetIds: [wellA] });
    return new Response(JSON.stringify(row), { status: 201 });
  };
  await assert.rejects(api.createGrant(draft, false, signal(), fetcher),
    { kind: 'invalid' });
  assert.equal(count, 0);
  const created = await api.createGrant(draft, true, signal(), fetcher);
  assert.deepEqual(created, row);
  assert.equal(count, 1);
});

test('failed/ambiguous writes cannot claim a saved assignment', async () => {
  for (const status of [400, 403, 409, 500]) {
    await assert.rejects(api.createGrant(draft, true, signal(),
      async () => new Response('{}', { status })), {
      kind: status === 400 ? 'invalid' : status === 403 ? 'denied'
        : status === 409 ? 'conflict' : 'unavailable',
    });
  }
  for (const body of [{ ...row, assetIds: [] },
    { ...row, userId: roleId }, { ...row, isActive: false }]) {
    await assert.rejects(api.createGrant(draft, true, signal(),
      async () => new Response(JSON.stringify(body), { status: 201 })),
    { kind: 'invalidResponse' });
  }
});

test('Admin Grant page initially shows only loading and no prior subject data', () => {
  const html = renderToStaticMarkup(createElement(
    component.GrantAdministrationPage, { currentUserId: userId }));
  assert.match(html, /Loading authorized users/);
  assert.doesNotMatch(html, /FIELD\\operator/);
});
