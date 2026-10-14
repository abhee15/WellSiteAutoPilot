/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, access;
before(async () => {
  vite = await createServer({
    server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent',
  });
  access = await vite.ssrLoadModule('/src/views/accessManagement.ts');
});
after(async () => { await vite?.close(); });

const userId = '725d06d3-e773-4534-b3e7-a4607c727196';
const adminId = '960bd27f-b843-48f9-9ead-59e4d8b6cc6b';
const wellId = '01468723-b10f-4107-8281-2d61c9b1b3e7';
const record = {
  userId, identityName: 'FIELD\\operator', displayName: 'Operator',
  isActive: true, roles: ['Operator'], assetScopeIds: [wellId],
  createdAtUtc: '2026-10-14T10:00:00Z', lastSeenAtUtc: '2026-10-14T11:00:00Z',
};

test('admin-only user listing uses uncached abortable same-origin API', async () => {
  const controller = new AbortController();
  const result = await access.loadSecurityUsers(controller.signal, async (url, init) => {
    assert.equal(url, '/api/v1/security/users?limit=100');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify([record]));
  });
  assert.deepEqual(result, [record]);
});

test('denial and malformed responses reject without exposing stale accounts', async () => {
  for (const status of [401, 403, 404, 409, 500]) {
    await assert.rejects(access.loadSecurityUsers(new AbortController().signal,
      async () => new Response('{}', { status })), {
      kind: status === 401 || status === 403 ? 'accessDenied'
        : status === 404 ? 'notFound' : status === 409 ? 'conflict' : 'unavailable',
    });
  }
  for (const body of ['{}', '[{"userId":"x"}]', 'not-json',
    JSON.stringify([record, record])]) {
    await assert.rejects(access.loadSecurityUsers(new AbortController().signal,
      async () => new Response(body)), { kind: 'invalidResponse' });
  }
});

test('scope entry validates UUIDs and avoids duplicate assignment', () => {
  assert.deepEqual(access.parseScopeIds(' ' + wellId + ';' + wellId.toUpperCase()),
    [wellId]);
  assert.deepEqual(access.parseScopeIds(''), []);
  assert.throws(() => access.parseScopeIds('well-123'), { kind: 'invalid' });
});

test('no unconfirmed, unchanged or self access updates can write', async () => {
  let requests = 0;
  const fetcher = async () => { requests++; return new Response('{}'); };
  for (const [current, roles, scopes, confirmed] of [
    [adminId, ['Engineer'], wellId, false],
    [userId, ['Engineer'], wellId, true],
    [adminId, ['Operator'], wellId, true],
    [adminId, ['Engineer'], 'not-a-guid', true],
  ]) {
    await assert.rejects(access.replaceUserAccess(record, current, roles, scopes,
      confirmed, new AbortController().signal, fetcher), { kind: 'invalid' });
  }
  assert.equal(requests, 0);
});

test('confirmed update is single PUT, checks identity and returned access', async () => {
  let calls = 0;
  const controller = new AbortController();
  const update = { ...record, roles: ['Engineer'] };
  const result = await access.replaceUserAccess(record, adminId, ['Engineer'], wellId,
    true, controller.signal, async (url, init) => {
      calls++;
      assert.equal(url, '/api/v1/security/users/' + userId + '/access');
      assert.equal(init.method, 'PUT');
      assert.equal(init.credentials, 'same-origin');
      assert.equal(init.cache, 'no-store');
      assert.equal(init.signal, controller.signal);
      assert.deepEqual(JSON.parse(init.body), { roles: ['Engineer'], assetScopeIds: [wellId] });
      return new Response(JSON.stringify(update));
    });
  assert.equal(calls, 1);
  assert.deepEqual(result, update);
});

test('conflicts and ambiguous 2xx replies never claim a successful access change', async () => {
  for (const [status, kind] of [[403, 'accessDenied'], [409, 'conflict'], [500, 'unavailable']]) {
    await assert.rejects(access.replaceUserAccess(record, adminId, ['Engineer'], wellId,
      true, new AbortController().signal, async () => new Response('{}', { status })), { kind });
  }
  for (const body of [JSON.stringify(record), JSON.stringify({ ...record, userId: adminId }), '{}']) {
    await assert.rejects(access.replaceUserAccess(record, adminId, ['Engineer'], wellId,
      true, new AbortController().signal, async () => new Response(body)), {
      kind: 'invalidResponse',
    });
  }
});
