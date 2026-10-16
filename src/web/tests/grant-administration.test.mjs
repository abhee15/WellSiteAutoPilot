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
  scopeKind: 'ExactAssets', assetIds: [wellA], expectedRoleRevision: 1 };
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
      scopeKind: 'ExactAssets', assetIds: [wellA], expectedRoleRevision: 1 });
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

test('deactivation needs explicit confirmation and matching next inactive revision', async () => {
  let requests = 0;
  const fetcher = async (url, init) => {
    requests++;
    assert.equal(url, '/api/v1/security/users/' + userId +
      '/grants/' + grantId + '/deactivate');
    assert.equal(init.method, 'PUT');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.deepEqual(JSON.parse(init.body), { expectedRevision: 1 });
    return new Response(JSON.stringify({
      ...row, isActive: false, revision: 2,
    }), { status: 200 });
  };
  await assert.rejects(api.deactivateGrant(row, false, signal(), fetcher),
    { kind: 'invalid' });
  assert.equal(requests, 0);
  assert.deepEqual(await api.deactivateGrant(row, true, signal(), fetcher),
    { ...row, isActive: false, revision: 2 });
  assert.equal(requests, 1);
  await assert.rejects(api.deactivateGrant(
    { ...row, isActive: false }, true, signal(), fetcher),
  { kind: 'invalid' });
  await assert.rejects(api.deactivateGrant(
    { ...row, revision: 0 }, true, signal(), fetcher),
  { kind: 'invalid' });
  assert.equal(requests, 1);
});

test('deactivation rejects stale, altered and ambiguous server outcomes', async () => {
  const invalid = [
    { ...row, isActive: true, revision: 2 },
    { ...row, isActive: false, revision: 1 },
    { ...row, isActive: false, revision: 3 },
    { ...row, isActive: false, revision: 2, grantId: roleId },
    { ...row, isActive: false, revision: 2, userId: roleId },
    { ...row, isActive: false, revision: 2, roleId: userId },
    { ...row, isActive: false, revision: 2, assetIds: [] },
  ];
  for (const candidate of invalid) {
    await assert.rejects(api.deactivateGrant(row, true, signal(),
      async () => new Response(JSON.stringify(candidate), { status: 200 })),
    { kind: 'invalidResponse' });
  }
  for (const [status, kind] of [
    [400, 'invalid'], [403, 'denied'], [404, 'notFound'],
    [409, 'conflict'], [500, 'unavailable'],
  ]) {
    await assert.rejects(api.deactivateGrant(row, true, signal(),
      async () => new Response('{}', { status })), { kind });
  }
  await assert.rejects(api.deactivateGrant(row, true, signal(),
    async () => { throw new TypeError('Connection lost after send'); }),
  { kind: 'unavailable' });
});

test('post-write readback must match the entire receipt before claiming success', () => {
  api.verifyGrantReadback(row, [{ ...row }]);
  for (const changed of [
    { ...row, roleId: userId }, { ...row, userId: roleId },
    { ...row, revision: 2 }, { ...row, isActive: false },
    { ...row, assetIds: [roleId] }, { ...row, scopeKind: 'AllAssets', assetIds: [] },
  ]) {
    assert.throws(() => api.verifyGrantReadback(row, [changed]), { kind: 'invalidResponse' });
  }
  assert.throws(() => api.verifyGrantReadback(row, []), { kind: 'invalidResponse' });
});

test('Windows group assignments keep subject identity through create, readback and retirement', async () => {
  const groupSid = 'S-1-5-21-100-200-300-400';
  const groupDraft = { ...draft, subjectKind: 'WindowsGroup', userId: '', groupSid };
  const groupRow = { ...row, subjectKind: 'WindowsGroup', userId: null, groupSid };
  const path = '/api/v1/security/windows-groups/' + groupSid + '/grants';
  const saved = await api.createGrant(groupDraft, true, signal(), async (url, init) => {
    assert.equal(url, path);
    assert.equal(init.method, 'POST');
    assert.equal(JSON.parse(init.body).groupSid, undefined);
    return new Response(JSON.stringify(groupRow), { status: 201 });
  });
  assert.equal(saved.groupSid, groupSid);
  const rows = await api.loadGrants(groupSid, signal(), async (url) => {
    assert.equal(url, path); return new Response(JSON.stringify([groupRow]));
  });
  api.verifyGrantReadback(groupRow, rows);
  const retired = { ...groupRow, isActive: false, revision: 2 };
  assert.deepEqual(await api.deactivateGrant(groupRow, true, signal(), async (url) => {
    assert.equal(url, path + '/' + grantId + '/deactivate');
    return new Response(JSON.stringify(retired));
  }), retired);
  for (const bad of [row, { ...groupRow, groupSid: 'S-1-5-21-100-200-300-401' },
    { ...groupRow, userId }, { ...groupRow, groupSid: groupSid.toLowerCase() }]) {
    await assert.rejects(api.loadGrants(groupSid, signal(), async () => new Response(JSON.stringify([bad]))),
      { kind: 'invalidResponse' });
    await assert.rejects(api.createGrant(groupDraft, true, signal(), async () => new Response(JSON.stringify(bad), { status: 201 })),
      { kind: 'invalidResponse' });
  }
  for (const badSid of ['FIELD\\Operators', 'S-1-5-01', 'S-1-5-4294967296', ' S-1-5-21']) {
    await assert.rejects(api.createGrant({ ...groupDraft, groupSid: badSid }, true, signal(),
      async () => { throw new Error('Invalid subject reached network'); }), { kind: 'invalid' });
  }
});
